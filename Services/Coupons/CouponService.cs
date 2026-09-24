using Microsoft.EntityFrameworkCore;
using ZokaAnalytics.Data;
using ZokaAnalytics.Models;
using ZokaAnalytics.Services.Markets;

namespace ZokaAnalytics.Services.Coupons;

public class CouponService : ICouponService
{
    private const int MaxSelections = 12;
    private const decimal Margin = 1.06m;

    private readonly ApplicationDbContext _db;
    private readonly ILogger<CouponService> _logger;

    public CouponService(ApplicationDbContext db, ILogger<CouponService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<CouponResult> CreateAsync(
        string userId, string title, decimal stake, List<SelectionInput> picks, CancellationToken ct = default)
    {
        if (picks.Count == 0)
            return new CouponResult(false, null, "En az bir mac secmelisiniz.");

        if (picks.Count > MaxSelections)
            return new CouponResult(false, null, $"En fazla {MaxSelections} mac secebilirsiniz.");

        if (stake <= 0)
            return new CouponResult(false, null, "Gecerli bir tutar girin.");

        var duplicates = picks.GroupBy(p => p.MatchId).Any(g => g.Count() > 1);
        if (duplicates)
            return new CouponResult(false, null, "Ayni mactan birden fazla secim yapamazsiniz.");

        var matchIds = picks.Select(p => p.MatchId).ToList();
        var matches = await _db.Matches
            .Include(m => m.HomeTeam)
            .Include(m => m.AwayTeam)
            .Include(m => m.Prediction)
            .Where(m => matchIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, ct);

        if (matches.Count != picks.Count)
            return new CouponResult(false, null, "Bazi maclar bulunamadi.");

        var started = matches.Values.Where(m => m.Status != MatchStatus.Scheduled).ToList();
        if (started.Count > 0)
            return new CouponResult(false, null, $"Baslamis mac secilemez: {started[0].HomeTeam.Name}");

        var coupon = new Coupon
        {
            UserId = userId,
            Title = string.IsNullOrWhiteSpace(title) ? "Kuponum" : title.Trim(),
            Stake = stake,
            Status = CouponStatus.Open,
            CreatedAtUtc = DateTime.UtcNow
        };

        decimal totalOdds = 1m;

        foreach (var pick in picks)
        {
            var match = matches[pick.MatchId];
            var odds = CalculateOdds(match, pick.Selection);

            if (odds is null)
                return new CouponResult(false, null,
                    $"{match.HomeTeam.Name} - {match.AwayTeam.Name} icin oran hesaplanamadi (tahmin yok).");

            totalOdds *= odds.Value;

            coupon.Items.Add(new CouponItem
            {
                MatchId = match.Id,
                Selection = pick.Selection,
                SelectionLabel = BuildLabel(match, pick.Selection),
                Odds = odds.Value,
                Status = SelectionStatus.Pending
            });
        }

        coupon.TotalOdds = Math.Round(totalOdds, 2);
        coupon.PotentialReturn = Math.Round(stake * coupon.TotalOdds, 2);

        _db.Coupons.Add(coupon);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Kupon olusturuldu: #{Id}, {Count} secim, toplam oran {Odds}",
            coupon.Id, coupon.Items.Count, coupon.TotalOdds);

        return new CouponResult(true, coupon.Id, null);
    }

    public Task<List<Coupon>> GetUserCouponsAsync(string userId, CancellationToken ct = default)
        => _db.Coupons
            .Include(c => c.Items).ThenInclude(i => i.Match).ThenInclude(m => m.HomeTeam)
            .Include(c => c.Items).ThenInclude(i => i.Match).ThenInclude(m => m.AwayTeam)
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.CreatedAtUtc)
            .AsNoTracking()
            .ToListAsync(ct);

    public Task<Coupon?> GetAsync(int couponId, string userId, CancellationToken ct = default)
        => _db.Coupons
            .Include(c => c.Items).ThenInclude(i => i.Match).ThenInclude(m => m.HomeTeam)
            .Include(c => c.Items).ThenInclude(i => i.Match).ThenInclude(m => m.AwayTeam)
            .Include(c => c.Items).ThenInclude(i => i.Match).ThenInclude(m => m.League)
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == couponId && c.UserId == userId, ct);

    public async Task<bool> DeleteAsync(int couponId, string userId, CancellationToken ct = default)
    {
        var coupon = await _db.Coupons
            .FirstOrDefaultAsync(c => c.Id == couponId && c.UserId == userId, ct);

        if (coupon is null) return false;

        _db.Coupons.Remove(coupon);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<int> SettleAllAsync(CancellationToken ct = default)
    {
        var openCoupons = await _db.Coupons
            .Include(c => c.Items).ThenInclude(i => i.Match)
            .Where(c => c.Status == CouponStatus.Open)
            .ToListAsync(ct);

        var settled = 0;

        foreach (var coupon in openCoupons)
        {
            foreach (var item in coupon.Items.Where(i => i.Status == SelectionStatus.Pending))
            {
                var match = item.Match;

                if (match.Status == MatchStatus.Cancelled || match.Status == MatchStatus.Abandoned)
                {
                    item.Status = SelectionStatus.Void;
                    continue;
                }

                if (match.Status != MatchStatus.Finished || match.HomeGoals is null || match.AwayGoals is null)
                    continue;

                item.Status = Evaluate(item.Selection, match.HomeGoals.Value, match.AwayGoals.Value)
                    ? SelectionStatus.Won
                    : SelectionStatus.Lost;
            }

            if (coupon.Items.Any(i => i.Status == SelectionStatus.Lost))
            {
                coupon.Status = CouponStatus.Lost;
                coupon.SettledAtUtc = DateTime.UtcNow;
                settled++;
            }
            else if (coupon.Items.All(i => i.Status is SelectionStatus.Won or SelectionStatus.Void))
            {
                coupon.Status = CouponStatus.Won;
                coupon.SettledAtUtc = DateTime.UtcNow;

                var effectiveOdds = coupon.Items
                    .Where(i => i.Status == SelectionStatus.Won)
                    .Aggregate(1m, (acc, i) => acc * i.Odds);

                coupon.TotalOdds = Math.Round(effectiveOdds, 2);
                coupon.PotentialReturn = Math.Round(coupon.Stake * coupon.TotalOdds, 2);
                settled++;
            }
        }

        await _db.SaveChangesAsync(ct);
        return settled;
    }

    public async Task<List<(int, BetType, string, decimal, double)>> BuildSuggestionsAsync(
        int count, CancellationToken ct = default)
    {
        var candidates = await _db.Matches
            .Include(m => m.HomeTeam)
            .Include(m => m.AwayTeam)
            .Include(m => m.Prediction)
            .Where(m => m.Status == MatchStatus.Scheduled
                        && m.KickoffUtc > DateTime.UtcNow
                        && m.Prediction != null)
            .OrderByDescending(m => m.Prediction!.Confidence)
            .Take(count * 3)
            .AsNoTracking()
            .ToListAsync(ct);

        var result = new List<(int, BetType, string, decimal, double)>();

        foreach (var match in candidates)
        {
            var p = match.Prediction!;

            var best = new[]
            {
                (Type: BetType.HomeWin, Prob: p.HomeWinProbability),
                (Type: BetType.Draw, Prob: p.DrawProbability),
                (Type: BetType.AwayWin, Prob: p.AwayWinProbability)
            }.OrderByDescending(x => x.Prob).First();

            BetType selection;
            double probability;

            if (best.Prob >= 0.50)
            {
                selection = best.Type;
                probability = best.Prob;
            }
            else if (p.HomeWinProbability + p.DrawProbability >= p.DrawProbability + p.AwayWinProbability)
            {
                selection = BetType.DoubleChance1X;
                probability = p.HomeWinProbability + p.DrawProbability;
            }
            else
            {
                selection = BetType.DoubleChanceX2;
                probability = p.DrawProbability + p.AwayWinProbability;
            }

            var odds = ProbabilityToOdds(probability);
            if (odds is null) continue;

            result.Add((match.Id, selection, BuildLabel(match, selection), odds.Value, probability));

            if (result.Count >= count) break;
        }

        return result;
    }

    public static decimal? CalculateOdds(Match match, BetType selection)
    {
        var p = match.Prediction;
        if (p is null) return null;

        var probability = PoissonMarkets.ProbabilityFor(
            selection,
            p.ExpectedHomeGoals,
            p.ExpectedAwayGoals,
            p.HomeWinProbability,
            p.DrawProbability,
            p.AwayWinProbability);

        return ProbabilityToOdds(probability);
    }

    private static decimal? ProbabilityToOdds(double probability)
    {
        if (probability is <= 0.02 or >= 0.99) return null;
        return Math.Round((decimal)(1.0 / probability) / Margin, 2);
    }

    private static bool Evaluate(BetType selection, int homeGoals, int awayGoals)
    {
        var total = homeGoals + awayGoals;

        return selection switch
        {
            BetType.HomeWin => homeGoals > awayGoals,
            BetType.Draw => homeGoals == awayGoals,
            BetType.AwayWin => homeGoals < awayGoals,
            BetType.DoubleChance1X => homeGoals >= awayGoals,
            BetType.DoubleChanceX2 => homeGoals <= awayGoals,
            BetType.DoubleChance12 => homeGoals != awayGoals,
            BetType.Over15 => total > 1,
            BetType.Under15 => total <= 1,
            BetType.Over25 => total > 2,
            BetType.Over35 => total > 3,
            BetType.Under35 => total <= 3,
            BetType.Under25 => total <= 2,
            BetType.BttsYes => homeGoals > 0 && awayGoals > 0,
            BetType.BttsNo => homeGoals == 0 || awayGoals == 0,
            _ => false
        };
    }

    public static string BuildLabel(Match match, BetType selection) => selection switch
    {
        BetType.HomeWin => $"{match.HomeTeam.Name} kazanir",
        BetType.Draw => "Beraberlik",
        BetType.AwayWin => $"{match.AwayTeam.Name} kazanir",
        BetType.DoubleChance1X => $"{match.HomeTeam.Name} veya beraberlik",
        BetType.DoubleChanceX2 => $"{match.AwayTeam.Name} veya beraberlik",
        BetType.DoubleChance12 => "Beraberlik disinda",
        BetType.Over15 => "1.5 Ust",
        BetType.Under15 => "1.5 Alt",
        BetType.Over25 => "2.5 Ust",
        BetType.Under25 => "2.5 Alt",
        BetType.Over35 => "3.5 Ust",
        BetType.Under35 => "3.5 Alt",
        BetType.BttsYes => "Karsilikli gol var",
        BetType.BttsNo => "Karsilikli gol yok",
        _ => selection.ToString()
    };
}
