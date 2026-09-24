using Microsoft.EntityFrameworkCore;
using ZokaAnalytics.Data;
using ZokaAnalytics.Models;
using ZokaAnalytics.Services.Markets;

namespace ZokaAnalytics.Services.Coupons;

public record WeeklyPick(
    int MatchId,
    string HomeTeamName,
    string AwayTeamName,
    string LeagueName,
    DateTime KickoffUtc,
    BetType Selection,
    string Label,
    decimal Odds,
    double Probability);

public record WeeklyCoupon(
    string Name,
    string Description,
    List<WeeklyPick> Picks,
    decimal TotalOdds,
    double CombinedProbability);

public interface IWeeklyCouponBuilder
{
    Task<List<WeeklyCoupon>> BuildAsync(int days = 7, CancellationToken ct = default);
}

public class WeeklyCouponBuilder : IWeeklyCouponBuilder
{
    private const decimal Margin = 1.06m;

    private readonly ApplicationDbContext _db;

    public WeeklyCouponBuilder(ApplicationDbContext db) => _db = db;

    public async Task<List<WeeklyCoupon>> BuildAsync(int days = 7, CancellationToken ct = default)
    {
        var horizon = DateTime.UtcNow.AddDays(days);

        var matches = await _db.Matches
            .Include(m => m.HomeTeam)
            .Include(m => m.AwayTeam)
            .Include(m => m.League)
            .Include(m => m.Prediction)
            .Where(m => m.Status == MatchStatus.Scheduled
                        && m.KickoffUtc > DateTime.UtcNow
                        && m.KickoffUtc <= horizon
                        && m.Prediction != null)
            .OrderBy(m => m.KickoffUtc)
            .AsNoTracking()
            .ToListAsync(ct);

        if (matches.Count == 0) return new List<WeeklyCoupon>();

        var candidates = new List<WeeklyPick>();

        var markets = new[]
        {
            BetType.HomeWin, BetType.Draw, BetType.AwayWin,
            BetType.DoubleChance1X, BetType.DoubleChanceX2, BetType.DoubleChance12,
            BetType.Over15, BetType.Under15,
            BetType.Over25, BetType.Under25,
            BetType.Over35, BetType.Under35,
            BetType.BttsYes, BetType.BttsNo
        };

        foreach (var match in matches)
        {
            var p = match.Prediction!;

            foreach (var market in markets)
            {
                var probability = PoissonMarkets.ProbabilityFor(
                    market, p.ExpectedHomeGoals, p.ExpectedAwayGoals,
                    p.HomeWinProbability, p.DrawProbability, p.AwayWinProbability);

                if (probability is <= 0.02 or >= 0.97) continue;

                var odds = Math.Round((decimal)(1.0 / probability) / Margin, 2);

                candidates.Add(new WeeklyPick(
                    match.Id,
                    match.HomeTeam.Name,
                    match.AwayTeam.Name,
                    match.League.Name,
                    match.KickoffUtc,
                    market,
                    CouponService.BuildLabel(match, market),
                    odds,
                    probability));
            }
        }

        var coupons = new List<WeeklyCoupon>
        {
            Build("Güvenli", "En yüksek olasılıklı 4 seçim. Düşük oran, yüksek tutma ihtimali.",
                candidates, minProbability: 0.72, count: 4),

            Build("Dengeli", "Olasılık ve oran dengesi gözetilen 5 seçim.",
                candidates, minProbability: 0.58, count: 5),

            Build("Riskli", "Yüksek oranlı 6 seçim. Tutma ihtimali düşük, kazanç yüksek.",
                candidates, minProbability: 0.42, count: 6)
        };

        return coupons.Where(c => c.Picks.Count > 0).ToList();
    }

    private static WeeklyCoupon Build(
        string name, string description, List<WeeklyPick> candidates,
        double minProbability, int count)
    {
        var used = new HashSet<int>();
        var picks = new List<WeeklyPick>();

        var pool = candidates
            .Where(c => c.Probability >= minProbability)
            .OrderByDescending(c => c.Odds)
            .ThenByDescending(c => c.Probability)
            .ToList();

        foreach (var pick in pool)
        {
            if (used.Contains(pick.MatchId)) continue;

            picks.Add(pick);
            used.Add(pick.MatchId);

            if (picks.Count >= count) break;
        }

        if (picks.Count < count)
        {
            foreach (var pick in candidates
                         .Where(c => c.Probability >= minProbability - 0.12)
                         .OrderByDescending(c => c.Probability))
            {
                if (used.Contains(pick.MatchId)) continue;

                picks.Add(pick);
                used.Add(pick.MatchId);

                if (picks.Count >= count) break;
            }
        }

        picks = picks.OrderBy(p => p.KickoffUtc).ToList();

        var totalOdds = picks.Aggregate(1m, (acc, p) => acc * p.Odds);
        var combined = picks.Aggregate(1.0, (acc, p) => acc * p.Probability);

        return new WeeklyCoupon(name, description, picks, Math.Round(totalOdds, 2), combined);
    }
}
