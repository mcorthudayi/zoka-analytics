using Microsoft.EntityFrameworkCore;
using Microsoft.ML;
using ZokaAnalytics.Data;
using ZokaAnalytics.Models;
using ZokaAnalytics.Services.Analysis;

namespace ZokaAnalytics.Services.Prediction;

public class MatchPredictionEngine : IMatchPredictionEngine
{
    public const string CurrentModelVersion = "v1";

    private readonly ApplicationDbContext _db;
    private readonly ITeamStrengthService _strength;
    private readonly IFormService _form;
    private readonly IH2HService _h2h;
    private readonly IMlModelStore _modelStore;
    private readonly ILogger<MatchPredictionEngine> _logger;

    public MatchPredictionEngine(
        ApplicationDbContext db,
        ITeamStrengthService strength,
        IFormService form,
        IH2HService h2h,
        IMlModelStore modelStore,
        ILogger<MatchPredictionEngine> logger)
    {
        _db = db;
        _strength = strength;
        _form = form;
        _h2h = h2h;
        _modelStore = modelStore;
        _logger = logger;
    }

    public async Task<Models.Prediction?> PredictAsync(int matchId, bool forceRefresh = false, CancellationToken ct = default)
    {
        var match = await _db.Matches
            .Include(m => m.Prediction)
            .Include(m => m.HomeTeam)
            .Include(m => m.AwayTeam)
            .FirstOrDefaultAsync(m => m.Id == matchId, ct);

        if (match is null) return null;
        if (match.Prediction is not null && !forceRefresh) return match.Prediction;

        var features = await BuildFeaturesAsync(match, ct);
        if (features is null)
        {
            _logger.LogWarning("Mac {MatchId} icin yeterli istatistik yok.", matchId);
            return null;
        }

        var (homeProb, drawProb, awayProb) = RunModel(features);
        var (xgHome, xgAway) = ExpectedGoals(features);

        var context = await _db.MatchContexts.AsNoTracking()
            .FirstOrDefaultAsync(c => c.MatchId == matchId, ct);

        if (context is not null && context.HasAnyAdjustment)
        {
            xgHome *= context.HomeXgMultiplier;
            xgAway *= context.AwayXgMultiplier;

            var adjusted = PoissonFromXg(xgHome, xgAway);

            homeProb = homeProb * 0.45 + adjusted.home * 0.55;
            drawProb = drawProb * 0.45 + adjusted.draw * 0.55;
            awayProb = awayProb * 0.45 + adjusted.away * 0.55;

            if (context.IsDerby)
            {
                drawProb *= 1.10;
                homeProb *= 0.95;
                awayProb *= 0.95;
            }

            var sum = homeProb + drawProb + awayProb;
            homeProb /= sum; drawProb /= sum; awayProb /= sum;
        }
        var over25 = PoissonOver25(xgHome, xgAway);
        var btts = PoissonBtts(xgHome, xgAway);

        var prediction = match.Prediction ?? new Models.Prediction { MatchId = match.Id };

        prediction.HomeWinProbability = Math.Round(homeProb, 4);
        prediction.DrawProbability = Math.Round(drawProb, 4);
        prediction.AwayWinProbability = Math.Round(awayProb, 4);
        prediction.Over25Probability = Math.Round(over25, 4);
        prediction.BttsProbability = Math.Round(btts, 4);
        prediction.ExpectedHomeGoals = Math.Round(xgHome, 2);
        prediction.ExpectedAwayGoals = Math.Round(xgAway, 2);

        var ordered = new[]
        {
            (Outcome: MatchOutcome.HomeWin, Prob: homeProb),
            (Outcome: MatchOutcome.Draw, Prob: drawProb),
            (Outcome: MatchOutcome.AwayWin, Prob: awayProb)
        }.OrderByDescending(x => x.Prob).ToArray();

        prediction.PredictedOutcome = ordered[0].Outcome;
        prediction.Confidence = Math.Round((ordered[0].Prob - ordered[1].Prob) * 100, 1);
        prediction.ModelVersion = _modelStore.IsLoaded ? CurrentModelVersion : CurrentModelVersion + "-poisson";
        prediction.GeneratedAtUtc = DateTime.UtcNow;
        prediction.IsSettled = false;
        prediction.WasCorrect = null;

        if (match.Prediction is null) _db.Predictions.Add(prediction);

        await _db.SaveChangesAsync(ct);
        return prediction;
    }

    public async Task<int> PredictUpcomingAsync(int days = 7, CancellationToken ct = default)
    {
        var horizon = DateTime.UtcNow.AddDays(days);

        var matchIds = await _db.Matches
            .Where(m => m.KickoffUtc >= DateTime.UtcNow
                        && m.KickoffUtc <= horizon
                        && m.Status == MatchStatus.Scheduled)
            .OrderBy(m => m.KickoffUtc)
            .Select(m => m.Id)
            .ToListAsync(ct);

        var created = 0;
        foreach (var id in matchIds)
        {
            var result = await PredictAsync(id, true, ct);
            if (result is not null) created++;
        }

        _logger.LogInformation("{Created}/{Total} mac icin tahmin uretildi.", created, matchIds.Count);
        return created;
    }

    public async Task<int> SettlePredictionsAsync(CancellationToken ct = default)
    {
        var pending = await _db.Predictions
            .Include(p => p.Match)
            .Where(p => !p.IsSettled && p.Match.Status == MatchStatus.Finished
                        && p.Match.HomeGoals != null && p.Match.AwayGoals != null)
            .ToListAsync(ct);

        foreach (var p in pending)
        {
            var actual = p.Match.Outcome;
            if (actual is null) continue;

            p.WasCorrect = p.PredictedOutcome == actual.Value;
            p.IsSettled = true;
        }

        await _db.SaveChangesAsync(ct);
        return pending.Count;
    }

    private async Task<MatchFeatures?> BuildFeaturesAsync(Match match, CancellationToken ct)
    {
        var homeStrength = await _strength.GetStrengthAsync(match.HomeTeamId, match.LeagueId, match.Season, ct);
        var awayStrength = await _strength.GetStrengthAsync(match.AwayTeamId, match.LeagueId, match.Season, ct);

        if (homeStrength is null && awayStrength is null) return null;

        homeStrength ??= NeutralStrength(match.HomeTeamId, match.HomeTeam?.Name);
        awayStrength ??= NeutralStrength(match.AwayTeamId, match.AwayTeam?.Name);

        var homeForm = await _form.GetFormAsync(match.HomeTeamId, 5, ct);
        var awayForm = await _form.GetFormAsync(match.AwayTeamId, 5, ct);
        var h2h = await _h2h.GetH2HAsync(match.HomeTeamId, match.AwayTeamId, 10, ct);

        var (homeRest, homeCongestion) = await RestProfileAsync(match.HomeTeamId, match.KickoffUtc, ct);
        var (awayRest, awayCongestion) = await RestProfileAsync(match.AwayTeamId, match.KickoffUtc, ct);
        var table = await TableSnapshotAsync(match.LeagueId, match.Season, ct);

        var homeRow = table.FirstOrDefault(t => t.TeamId == match.HomeTeamId);
        var awayRow = table.FirstOrDefault(t => t.TeamId == match.AwayTeamId);

        var leaderPoints = table.Count > 0 ? table[0].Points : 0;
        var dropIdx = Math.Max(0, table.Count - 4);
        var dropPoints = table.Count > dropIdx ? table[dropIdx].Points : 0;

        var avgPlayed = table.Count > 0 ? table.Average(t => t.Played) : 0;
        var progress = Math.Clamp(avgPlayed / 34.0, 0, 1);

        return new MatchFeatures
        {
            HomeStrength = (float)homeStrength.OverallStrength,
            HomeAttack = (float)homeStrength.AttackStrength,
            HomeDefence = (float)homeStrength.DefenceStrength,
            HomeVenueStrength = (float)homeStrength.HomeStrength,
            HomeFormScore = (float)(homeForm?.FormScore ?? 50),
            HomeAvgGoalsFor = (float)homeStrength.AvgGoalsFor,
            HomeAvgGoalsAgainst = (float)homeStrength.AvgGoalsAgainst,
            HomePointsPerGame = (float)homeStrength.PointsPerGame,

            AwayStrength = (float)awayStrength.OverallStrength,
            AwayAttack = (float)awayStrength.AttackStrength,
            AwayDefence = (float)awayStrength.DefenceStrength,
            AwayVenueStrength = (float)awayStrength.AwayStrength,
            AwayFormScore = (float)(awayForm?.FormScore ?? 50),
            AwayAvgGoalsFor = (float)awayStrength.AvgGoalsFor,
            AwayAvgGoalsAgainst = (float)awayStrength.AvgGoalsAgainst,
            AwayPointsPerGame = (float)awayStrength.PointsPerGame,

            HomeRank = homeRow is null ? 10 : table.IndexOf(homeRow) + 1,
            AwayRank = awayRow is null ? 10 : table.IndexOf(awayRow) + 1,
            HomePointsBehindLeader = leaderPoints - (homeRow?.Points ?? 0),
            AwayPointsBehindLeader = leaderPoints - (awayRow?.Points ?? 0),
            HomePointsAboveDrop = (homeRow?.Points ?? 0) - dropPoints,
            AwayPointsAboveDrop = (awayRow?.Points ?? 0) - dropPoints,
            SeasonProgress = (float)progress,

            HomeRestDays = (float)homeRest,
            AwayRestDays = (float)awayRest,
            RestDiff = (float)(homeRest - awayRest),
            HomeCongestion = homeCongestion,
            AwayCongestion = awayCongestion,

            StrengthDiff = (float)(homeStrength.OverallStrength - awayStrength.OverallStrength),
            FormDiff = (float)((homeForm?.FormScore ?? 50) - (awayForm?.FormScore ?? 50)),

            H2HMatches = h2h.TotalMatches,
            H2HHomeWinRate = h2h.TotalMatches > 0 ? (float)h2h.HomeTeamWins / h2h.TotalMatches : 0.4f,
            H2HDrawRate = h2h.TotalMatches > 0 ? (float)h2h.Draws / h2h.TotalMatches : 0.25f,
            H2HAvgGoals = h2h.TotalMatches > 0 ? (float)h2h.AvgTotalGoals : 2.6f,
            H2HOver25Rate = h2h.TotalMatches > 0 ? (float)h2h.Over25Rate : 0.5f,
            H2HBttsRate = h2h.TotalMatches > 0 ? (float)h2h.BttsRate : 0.5f
        };
    }

    private static TeamStrengthResult NeutralStrength(int teamId, string? name)
        => new(teamId, name ?? $"#{teamId}",
            OverallStrength: 50, AttackStrength: 50, DefenceStrength: 50,
            HomeStrength: 50, AwayStrength: 40,
            PointsPerGame: 1.35, AvgGoalsFor: 1.30, AvgGoalsAgainst: 1.30,
            MatchesPlayed: 0);

    private sealed record TableRow(int TeamId, int Points, int Played);

    private async Task<List<TableRow>> TableSnapshotAsync(int leagueId, int season, CancellationToken ct)
        => await _db.TeamStatistics
            .Where(s => s.LeagueId == leagueId && s.Season == season && s.PlayedTotal > 0)
            .OrderByDescending(s => s.Points)
            .ThenByDescending(s => s.GoalsForTotal - s.GoalsAgainstTotal)
            .Select(s => new TableRow(s.TeamId, s.Points, s.PlayedTotal))
            .AsNoTracking()
            .ToListAsync(ct);

    private async Task<(double RestDays, int Congestion)> RestProfileAsync(
        int teamId, DateTime kickoff, CancellationToken ct)
    {
        var recent = await _db.Matches
            .Where(m => (m.HomeTeamId == teamId || m.AwayTeamId == teamId)
                        && m.KickoffUtc < kickoff
                        && m.Status == MatchStatus.Finished)
            .OrderByDescending(m => m.KickoffUtc)
            .Select(m => m.KickoffUtc)
            .Take(10)
            .ToListAsync(ct);

        if (recent.Count == 0) return (7, 0);

        var rest = Math.Clamp((kickoff - recent[0]).TotalDays, 0, 14);
        var congestion = recent.Count(d => (kickoff - d).TotalDays <= 14);

        return (rest, congestion);
    }

    private (double home, double draw, double away) RunModel(MatchFeatures features)
    {
        if (_modelStore.IsLoaded)
        {
            try
            {
                var engine = _modelStore.GetPredictionEngine();
                var output = engine.Predict(features);

                if (output.Score.Length >= 3)
                {
                    var labels = _modelStore.GetLabels();
                    var home = ProbFor(labels, output.Score, "HomeWin");
                    var draw = ProbFor(labels, output.Score, "Draw");
                    var away = ProbFor(labels, output.Score, "AwayWin");

                    var sum = home + draw + away;
                    if (sum > 0)
                    {
                        var ml = (home / sum, draw / sum, away / sum);
                        return Blend(ml, PoissonOutcomes(features));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ML modeli calistirilamadi, Poisson'a dusuluyor.");
            }
        }

        return PoissonOutcomes(features);
    }

    private static (double home, double draw, double away) Blend(
        (double home, double draw, double away) ml,
        (double home, double draw, double away) poisson)
    {
        const double mlWeight = 0.6;

        var home = ml.home * mlWeight + poisson.home * (1 - mlWeight);
        var draw = ml.draw * mlWeight + poisson.draw * (1 - mlWeight);
        var away = ml.away * mlWeight + poisson.away * (1 - mlWeight);

        home = Math.Clamp(home, 0.04, 0.90);
        draw = Math.Clamp(draw, 0.06, 0.45);
        away = Math.Clamp(away, 0.04, 0.90);

        var total = home + draw + away;
        return (home / total, draw / total, away / total);
    }

    private static double ProbFor(string[] labels, float[] scores, string label)
    {
        var index = Array.IndexOf(labels, label);
        return index >= 0 && index < scores.Length ? Math.Max(0, scores[index]) : 0;
    }

    private static (double home, double away) ExpectedGoals(MatchFeatures f)
    {
        const double homeAdvantage = 1.15;

        var homeAttackFactor = Math.Max(0.3, f.HomeAttack / 50.0);
        var awayDefenceFactor = Math.Max(0.3, 100.0 / Math.Max(20, f.AwayDefence) / 2.0);
        var awayAttackFactor = Math.Max(0.3, f.AwayAttack / 50.0);
        var homeDefenceFactor = Math.Max(0.3, 100.0 / Math.Max(20, f.HomeDefence) / 2.0);

        var baseHome = (f.HomeAvgGoalsFor + f.AwayAvgGoalsAgainst) / 2.0;
        var baseAway = (f.AwayAvgGoalsFor + f.HomeAvgGoalsAgainst) / 2.0;

        if (baseHome <= 0) baseHome = 1.35;
        if (baseAway <= 0) baseAway = 1.1;

        var homeFormMult = 0.85 + (f.HomeFormScore / 100.0) * 0.3;
        var awayFormMult = 0.85 + (f.AwayFormScore / 100.0) * 0.3;

        var xgHome = baseHome * homeAdvantage * homeFormMult * (homeAttackFactor + awayDefenceFactor) / 2.0;
        var xgAway = baseAway * awayFormMult * (awayAttackFactor + homeDefenceFactor) / 2.0;

        return (Math.Clamp(xgHome, 0.2, 4.5), Math.Clamp(xgAway, 0.2, 4.5));
    }

    private static (double home, double draw, double away) PoissonOutcomes(MatchFeatures f)
    {
        var (xgHome, xgAway) = ExpectedGoals(f);
        return PoissonFromXg(xgHome, xgAway);
    }

    private static (double home, double draw, double away) PoissonFromXg(double xgHome, double xgAway)
    {

        double home = 0, draw = 0, away = 0;
        const int maxGoals = 8;

        for (var h = 0; h <= maxGoals; h++)
        {
            for (var a = 0; a <= maxGoals; a++)
            {
                var p = Poisson(h, xgHome) * Poisson(a, xgAway);
                if (h > a) home += p;
                else if (h == a) draw += p;
                else away += p;
            }
        }

        var total = home + draw + away;
        if (total <= 0) return (0.40, 0.27, 0.33);

        return (home / total, draw / total, away / total);
    }

    private static double PoissonOver25(double xgHome, double xgAway)
    {
        double under = 0;
        for (var h = 0; h <= 2; h++)
            for (var a = 0; a <= 2 - h; a++)
                under += Poisson(h, xgHome) * Poisson(a, xgAway);
        return Math.Clamp(1 - under, 0, 1);
    }

    private static double PoissonBtts(double xgHome, double xgAway)
    {
        var homeScores = 1 - Poisson(0, xgHome);
        var awayScores = 1 - Poisson(0, xgAway);
        return Math.Clamp(homeScores * awayScores, 0, 1);
    }

    private static double Poisson(int k, double lambda)
    {
        if (lambda <= 0) return k == 0 ? 1 : 0;
        return Math.Exp(-lambda) * Math.Pow(lambda, k) / Factorial(k);
    }

    private static double Factorial(int n)
    {
        double result = 1;
        for (var i = 2; i <= n; i++) result *= i;
        return result;
    }
}
