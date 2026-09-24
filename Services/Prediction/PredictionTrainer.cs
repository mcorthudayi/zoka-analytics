using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.ML;
using Microsoft.ML.Data;
using ZokaAnalytics.Data;
using ZokaAnalytics.Models;

namespace ZokaAnalytics.Services.Prediction;

public class MlModelStore : IMlModelStore
{
    private readonly MLContext _mlContext = new(seed: 42);
    private readonly ILogger<MlModelStore> _logger;
    private readonly IWebHostEnvironment _env;

    private ITransformer? _model;
    private string[] _labels = { "AwayWin", "Draw", "HomeWin" };
    private PredictionEngine<MatchFeatures, MatchPredictionOutput>? _engine;
    private readonly object _lock = new();

    public MlModelStore(ILogger<MlModelStore> logger, IWebHostEnvironment env)
    {
        _logger = logger;
        _env = env;
    }

    public bool IsLoaded => _model is not null;

    private string ModelPath => Path.Combine(_env.ContentRootPath, "MLModels", "match-outcome.zip");
    private string LabelsPath => Path.Combine(_env.ContentRootPath, "MLModels", "labels.json");

    public PredictionEngine<MatchFeatures, MatchPredictionOutput> GetPredictionEngine()
    {
        lock (_lock)
        {
            if (_model is null) throw new InvalidOperationException("Model yuklu degil.");
            return _engine ??= _mlContext.Model.CreatePredictionEngine<MatchFeatures, MatchPredictionOutput>(_model);
        }
    }

    public string[] GetLabels() => _labels;

    public void Load(ITransformer model, DataViewSchema schema, string[] labels)
    {
        lock (_lock)
        {
            _model = model;
            _labels = labels;
            _engine = null;
        }
    }

    public void TryLoadFromDisk()
    {
        try
        {
            if (!File.Exists(ModelPath)) return;

            var model = _mlContext.Model.Load(ModelPath, out var schema);

            var labels = File.Exists(LabelsPath)
                ? JsonSerializer.Deserialize<string[]>(File.ReadAllText(LabelsPath)) ?? _labels
                : _labels;

            Load(model, schema, labels);
            _logger.LogInformation("ML modeli diskten yuklendi. Etiket sirasi: {Labels}", string.Join(", ", labels));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ML modeli diskten yuklenemedi, Poisson kullanilacak.");
        }
    }

    public void SaveToDisk(ITransformer model, DataViewSchema schema, string[] labels)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ModelPath)!);
            _mlContext.Model.Save(model, schema, ModelPath);
            File.WriteAllText(LabelsPath, JsonSerializer.Serialize(labels));
            _logger.LogInformation("ML modeli kaydedildi: {Path}", ModelPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ML modeli kaydedilemedi.");
        }
    }
}

public class PredictionTrainer : IPredictionTrainer
{
    private const int MinimumSamples = 100;

    private const int MinMatchesBeforeSample = 5;

    private readonly ApplicationDbContext _db;
    private readonly IMlModelStore _store;
    private readonly ILogger<PredictionTrainer> _logger;

    public PredictionTrainer(
        ApplicationDbContext db,
        IMlModelStore store,
        ILogger<PredictionTrainer> logger)
    {
        _db = db;
        _store = store;
        _logger = logger;
    }

    public async Task<TrainingResult> TrainAsync(CancellationToken ct = default)
    {
        var samples = await BuildTrainingSetAsync(ct);

        if (samples.Count < MinimumSamples)
        {
            var msg = $"Egitim icin yeterli veri yok ({samples.Count}/{MinimumSamples}).";
            _logger.LogWarning(msg);
            return new TrainingResult(false, samples.Count, 0, 0, msg);
        }

        var dist = samples.GroupBy(s => s.Label)
            .ToDictionary(g => g.Key, g => g.Count());

        var homeWinAvgDiff = samples.Where(s => s.Label == "HomeWin").Select(s => s.StrengthDiff).DefaultIfEmpty(0).Average();
        var awayWinAvgDiff = samples.Where(s => s.Label == "AwayWin").Select(s => s.StrengthDiff).DefaultIfEmpty(0).Average();

        _logger.LogInformation(
            "Sinif dagilimi: {Dist}. Ortalama guc farki -> HomeWin: {H:F2}, AwayWin: {A:F2}",
            string.Join(", ", dist.Select(kv => $"{kv.Key}={kv.Value}")),
            homeWinAvgDiff, awayWinAvgDiff);

        var mlContext = new MLContext(seed: 42);
        var data = mlContext.Data.LoadFromEnumerable(samples);

        var shuffled = mlContext.Data.ShuffleRows(data, seed: 42);
        var split = mlContext.Data.TrainTestSplit(shuffled, testFraction: 0.2, seed: 42);

        var featureColumns = typeof(MatchFeatures)
            .GetProperties()
            .Where(p => p.PropertyType == typeof(float))
            .Select(p => p.Name)
            .ToArray();

        var baseline = mlContext.Transforms.Conversion
            .MapValueToKey("LabelKey", nameof(MatchFeatures.Label))
            .Append(mlContext.Transforms.Concatenate("Features", featureColumns))
            .Append(mlContext.Transforms.NormalizeMinMax("Features"));

        var candidates = new (string Name, IEstimator<ITransformer> Trainer)[]
        {
            ("SDCA", mlContext.MulticlassClassification.Trainers
                .SdcaMaximumEntropy(labelColumnName: "LabelKey", featureColumnName: "Features",
                    l2Regularization: 0.01f, maximumNumberOfIterations: 200)),

            ("Lbfgs", mlContext.MulticlassClassification.Trainers
                .LbfgsMaximumEntropy(labelColumnName: "LabelKey", featureColumnName: "Features",
                    l1Regularization: 0.05f, l2Regularization: 0.05f, historySize: 40))
        };

        ITransformer? bestModel = null;
        string[] bestLabels = { "AwayWin", "Draw", "HomeWin" };
        var bestLogLoss = double.MaxValue;
        var bestAccuracy = 0.0;
        var bestName = "-";
        var report = new List<string>();

        foreach (var (name, trainer) in candidates)
        {
            try
            {
                var pipeline = baseline
                    .Append(trainer)
                    .Append(mlContext.Transforms.Conversion.MapKeyToValue("PredictedLabel", "PredictedLabel"));

                var model = pipeline.Fit(split.TrainSet);
                var scored = model.Transform(split.TestSet);
                var metrics = mlContext.MulticlassClassification.Evaluate(scored, labelColumnName: "LabelKey");

                report.Add($"{name}: acc {metrics.MicroAccuracy:P1} / macro {metrics.MacroAccuracy:P1} / logloss {metrics.LogLoss:F3}");
                _logger.LogInformation("{Name} -> {Report}", name, report[^1]);

                if (metrics.LogLoss < bestLogLoss)
                {
                    bestLogLoss = metrics.LogLoss;
                    bestAccuracy = metrics.MicroAccuracy;
                    bestModel = model;
                    bestLabels = ExtractScoreLabels(scored);
                    bestName = name;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "{Name} egitilemedi, atlaniyor.", name);
                report.Add($"{name}: HATA - {ex.Message}");
            }
        }

        if (bestModel is null)
            return new TrainingResult(false, samples.Count, 0, 0, "Hicbir model egitilemedi. " + string.Join(" | ", report));

        _store.Load(bestModel, split.TrainSet.Schema, bestLabels);
        _store.SaveToDisk(bestModel, split.TrainSet.Schema, bestLabels);

        var message =
            $"Secilen model: {bestName}. " +
            string.Join(" | ", report) +
            $". Etiket sirasi: {string.Join("/", bestLabels)}. " +
            $"Sinif dagilimi: {string.Join(", ", dist.Select(kv => $"{kv.Key}={kv.Value}"))}. " +
            "Referans: rastgele tahmin logloss 1.099, futbolda iyi model 0.95-1.02 bandindadir.";

        _logger.LogInformation(message);

        return new TrainingResult(true, samples.Count, bestAccuracy, bestLogLoss, message);
    }

    private static string[] ExtractScoreLabels(IDataView scored)
    {
        try
        {
            VBuffer<ReadOnlyMemory<char>> slots = default;
            scored.Schema["Score"].GetSlotNames(ref slots);

            var names = slots.DenseValues().Select(v => v.ToString()).ToArray();
            if (names.Length >= 3) return names;
        }
        catch
        {
        }

        return new[] { "AwayWin", "Draw", "HomeWin" };
    }

    private sealed class Running
    {
        public int Played, Wins, Draws, Losses, GoalsFor, GoalsAgainst, Points;
        public int PlayedHome, WinsHome, DrawsHome;
        public int PlayedAway, WinsAway, DrawsAway;
        public readonly List<char> Form = new();
        public DateTime? LastMatchUtc;
        public readonly List<DateTime> RecentDates = new();

        public double RestDays(DateTime kickoff)
            => LastMatchUtc is null ? 7 : Math.Clamp((kickoff - LastMatchUtc.Value).TotalDays, 0, 14);

        public int Congestion(DateTime kickoff)
            => RecentDates.Count(d => (kickoff - d).TotalDays <= 14);

        public void RecordFixture(DateTime kickoff)
        {
            LastMatchUtc = kickoff;
            RecentDates.Add(kickoff);
            RecentDates.RemoveAll(d => (kickoff - d).TotalDays > 21);
        }

        public double Ppg => Played > 0 ? (double)Points / Played : 0;
        public double Gf => Played > 0 ? (double)GoalsFor / Played : 0;
        public double Ga => Played > 0 ? (double)GoalsAgainst / Played : 0;
        public double HomePpg => PlayedHome > 0 ? (WinsHome * 3.0 + DrawsHome) / PlayedHome : 0;
        public double AwayPpg => PlayedAway > 0 ? (WinsAway * 3.0 + DrawsAway) / PlayedAway : 0;

        public double FormScore()
        {
            if (Form.Count == 0) return 50;

            double weighted = 0, max = 0;
            for (var i = 0; i < Form.Count; i++)
            {
                var w = i + 1;
                weighted += (Form[i] switch { 'W' => 3.0, 'D' => 1.0, _ => 0.0 }) * w;
                max += 3.0 * w;
            }
            return max > 0 ? weighted / max * 100 : 50;
        }

        public void Apply(int gf, int ga, bool isHome)
        {
            Played++;
            GoalsFor += gf;
            GoalsAgainst += ga;

            if (isHome) PlayedHome++; else PlayedAway++;

            char result;
            if (gf > ga)
            {
                result = 'W'; Wins++; Points += 3;
                if (isHome) WinsHome++; else WinsAway++;
            }
            else if (gf == ga)
            {
                result = 'D'; Draws++; Points += 1;
                if (isHome) DrawsHome++; else DrawsAway++;
            }
            else { result = 'L'; Losses++; }

            Form.Add(result);
            if (Form.Count > 5) Form.RemoveAt(0);
        }
    }

    private sealed class H2HRunning
    {
        public int Total, AWins, Draws, BWins, GoalSum, Over25, Btts;
    }

    private async Task<List<MatchFeatures>> BuildTrainingSetAsync(CancellationToken ct)
    {
        var matches = await _db.Matches
            .Where(m => m.Status == MatchStatus.Finished && m.HomeGoals != null && m.AwayGoals != null)
            .OrderBy(m => m.KickoffUtc)
            .Select(m => new
            {
                m.Id, m.LeagueId, m.Season, m.HomeTeamId, m.AwayTeamId, m.KickoffUtc,
                HomeGoals = m.HomeGoals!.Value, AwayGoals = m.AwayGoals!.Value
            })
            .AsNoTracking()
            .ToListAsync(ct);

        _logger.LogInformation("{Count} tamamlanmis mac uzerinden ozellik uretiliyor.", matches.Count);

        var samples = new List<MatchFeatures>(matches.Count);

        var teamState = new Dictionary<(int, int, int), Running>();
        var h2hState = new Dictionary<(int, int), H2HRunning>();

        foreach (var m in matches)
        {
            var homeKey = (m.LeagueId, m.Season, m.HomeTeamId);
            var awayKey = (m.LeagueId, m.Season, m.AwayTeamId);

            if (!teamState.TryGetValue(homeKey, out var home))
                teamState[homeKey] = home = new Running();
            if (!teamState.TryGetValue(awayKey, out var away))
                teamState[awayKey] = away = new Running();

            var pairKey = (Math.Min(m.HomeTeamId, m.AwayTeamId), Math.Max(m.HomeTeamId, m.AwayTeamId));
            if (!h2hState.TryGetValue(pairKey, out var h2h))
                h2hState[pairKey] = h2h = new H2HRunning();

            if (home.Played >= MinMatchesBeforeSample && away.Played >= MinMatchesBeforeSample)
            {
                var leagueTeams = teamState
                    .Where(kv => kv.Key.Item1 == m.LeagueId && kv.Key.Item2 == m.Season && kv.Value.Played > 0)
                    .Select(kv => kv.Value)
                    .ToList();

                var table = leagueTeams
                    .OrderByDescending(t => t.Points)
                    .ThenByDescending(t => t.GoalsFor - t.GoalsAgainst)
                    .ToList();

                var leaderPoints = table.Count > 0 ? table[0].Points : 0;
                var dropIndex = Math.Max(0, table.Count - 4);
                var dropPoints = table.Count > dropIndex ? table[dropIndex].Points : 0;

                var homeRank = table.IndexOf(home) + 1;
                var awayRank = table.IndexOf(away) + 1;

                var progress = Math.Clamp((home.Played + away.Played) / 2.0 / 34.0, 0, 1);

                var avgGf = leagueTeams.Average(t => t.Gf);
                var avgGa = leagueTeams.Average(t => t.Ga);
                var maxPpg = leagueTeams.Max(t => t.Ppg);

                var homeAttack = Score(home.Gf, avgGf);
                var homeDefence = InverseScore(home.Ga, avgGa);
                var awayAttack = Score(away.Gf, avgGf);
                var awayDefence = InverseScore(away.Ga, avgGa);

                var homeOverall = Overall(home, maxPpg, homeAttack, homeDefence);
                var awayOverall = Overall(away, maxPpg, awayAttack, awayDefence);

                var aIsHome = pairKey.Item1 == m.HomeTeamId;
                var h2hHomeWins = aIsHome ? h2h.AWins : h2h.BWins;

                samples.Add(new MatchFeatures
                {
                    HomeStrength = (float)homeOverall,
                    HomeAttack = (float)homeAttack,
                    HomeDefence = (float)homeDefence,
                    HomeVenueStrength = (float)Math.Clamp(home.HomePpg / 3.0 * 100, 0, 100),
                    HomeFormScore = (float)home.FormScore(),
                    HomeAvgGoalsFor = (float)home.Gf,
                    HomeAvgGoalsAgainst = (float)home.Ga,
                    HomePointsPerGame = (float)home.Ppg,

                    AwayStrength = (float)awayOverall,
                    AwayAttack = (float)awayAttack,
                    AwayDefence = (float)awayDefence,
                    AwayVenueStrength = (float)Math.Clamp(away.AwayPpg / 3.0 * 100, 0, 100),
                    AwayFormScore = (float)away.FormScore(),
                    AwayAvgGoalsFor = (float)away.Gf,
                    AwayAvgGoalsAgainst = (float)away.Ga,
                    AwayPointsPerGame = (float)away.Ppg,

                    HomeRank = homeRank > 0 ? homeRank : 10,
                    AwayRank = awayRank > 0 ? awayRank : 10,
                    HomePointsBehindLeader = leaderPoints - home.Points,
                    AwayPointsBehindLeader = leaderPoints - away.Points,
                    HomePointsAboveDrop = home.Points - dropPoints,
                    AwayPointsAboveDrop = away.Points - dropPoints,
                    SeasonProgress = (float)progress,

                    HomeRestDays = (float)home.RestDays(m.KickoffUtc),
                    AwayRestDays = (float)away.RestDays(m.KickoffUtc),
                    RestDiff = (float)(home.RestDays(m.KickoffUtc) - away.RestDays(m.KickoffUtc)),
                    HomeCongestion = home.Congestion(m.KickoffUtc),
                    AwayCongestion = away.Congestion(m.KickoffUtc),

                    StrengthDiff = (float)(homeOverall - awayOverall),
                    FormDiff = (float)(home.FormScore() - away.FormScore()),

                    H2HMatches = h2h.Total,
                    H2HHomeWinRate = h2h.Total > 0 ? (float)h2hHomeWins / h2h.Total : 0.4f,
                    H2HDrawRate = h2h.Total > 0 ? (float)h2h.Draws / h2h.Total : 0.25f,
                    H2HAvgGoals = h2h.Total > 0 ? (float)h2h.GoalSum / h2h.Total : 2.6f,
                    H2HOver25Rate = h2h.Total > 0 ? (float)h2h.Over25 / h2h.Total : 0.5f,
                    H2HBttsRate = h2h.Total > 0 ? (float)h2h.Btts / h2h.Total : 0.5f,

                    Label = m.HomeGoals > m.AwayGoals ? "HomeWin"
                          : m.HomeGoals < m.AwayGoals ? "AwayWin"
                          : "Draw"
                });
            }

            home.Apply(m.HomeGoals, m.AwayGoals, true);
            away.Apply(m.AwayGoals, m.HomeGoals, false);
            home.RecordFixture(m.KickoffUtc);
            away.RecordFixture(m.KickoffUtc);

            h2h.Total++;
            h2h.GoalSum += m.HomeGoals + m.AwayGoals;
            if (m.HomeGoals + m.AwayGoals > 2) h2h.Over25++;
            if (m.HomeGoals > 0 && m.AwayGoals > 0) h2h.Btts++;

            var aGoals = pairKey.Item1 == m.HomeTeamId ? m.HomeGoals : m.AwayGoals;
            var bGoals = pairKey.Item1 == m.HomeTeamId ? m.AwayGoals : m.HomeGoals;

            if (aGoals > bGoals) h2h.AWins++;
            else if (aGoals < bGoals) h2h.BWins++;
            else h2h.Draws++;
        }

        _logger.LogInformation("{Count} sizintisiz egitim ornegi uretildi.", samples.Count);
        return samples;
    }

    private static double Score(double value, double leagueAvg)
        => Math.Clamp((leagueAvg > 0 ? value / leagueAvg : 1) * 50, 0, 100);

    private static double InverseScore(double value, double leagueAvg)
        => Math.Clamp((value > 0 && leagueAvg > 0 ? leagueAvg / value : 2) * 50, 0, 100);

    private static double Overall(Running r, double maxPpg, double attack, double defence)
    {
        var pointsScore = maxPpg > 0 ? r.Ppg / maxPpg * 100 : 0;
        return Math.Clamp(pointsScore * 0.55 + attack * 0.225 + defence * 0.225, 0, 100);
    }
}
