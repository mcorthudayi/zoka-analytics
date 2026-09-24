using Microsoft.EntityFrameworkCore;
using ZokaAnalytics.Data;
using ZokaAnalytics.Models;

namespace ZokaAnalytics.Services.Prediction;

public class SubscriptionService : ISubscriptionService
{
    private readonly ApplicationDbContext _db;
    private readonly IConfiguration _configuration;

    public SubscriptionService(ApplicationDbContext db, IConfiguration configuration)
    {
        _db = db;
        _configuration = configuration;
    }

    public int GetDailyLimit(SubscriptionTier tier) => tier switch
    {
        SubscriptionTier.Free => _configuration.GetValue("Subscription:FreeDailyPredictionViews", 3),
        SubscriptionTier.Basic => _configuration.GetValue("Subscription:BasicDailyPredictionViews", 20),
        SubscriptionTier.Pro => _configuration.GetValue("Subscription:ProDailyPredictionViews", 100),
        SubscriptionTier.Elite => _configuration.GetValue("Subscription:EliteDailyPredictionViews", 0),
        _ => 3
    };

    public async Task<QuotaCheckResult> CheckQuotaAsync(string userId, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return new QuotaCheckResult(false, 0, 0, "Kullanici bulunamadi.");
        if (user.IsBanned) return new QuotaCheckResult(false, 0, 0, "Hesabiniz askiya alinmis.");

        ResetIfNewDay(user);

        var limit = GetDailyLimit(user.EffectiveTier);
        if (limit == 0) return new QuotaCheckResult(true, user.DailyPredictionViews, 0, null);

        var allowed = user.DailyPredictionViews < limit;
        return new QuotaCheckResult(allowed, user.DailyPredictionViews, limit,
            allowed ? null : "Gunluk tahmin hakkiniz doldu.");
    }

    public async Task<QuotaCheckResult> ConsumeQuotaAsync(string userId, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return new QuotaCheckResult(false, 0, 0, "Kullanici bulunamadi.");
        if (user.IsBanned) return new QuotaCheckResult(false, 0, 0, "Hesabiniz askiya alinmis.");

        ResetIfNewDay(user);

        var limit = GetDailyLimit(user.EffectiveTier);

        if (limit != 0 && user.DailyPredictionViews >= limit)
        {
            await _db.SaveChangesAsync(ct);
            return new QuotaCheckResult(false, user.DailyPredictionViews, limit,
                "Gunluk tahmin hakkiniz doldu.");
        }

        user.DailyPredictionViews++;
        await _db.SaveChangesAsync(ct);

        return new QuotaCheckResult(true, user.DailyPredictionViews, limit, null);
    }

    public async Task<bool> UpgradeAsync(string userId, SubscriptionTier tier, int months, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return false;

        var start = user.SubscriptionExpiresAtUtc.HasValue && user.SubscriptionExpiresAtUtc > DateTime.UtcNow
            ? user.SubscriptionExpiresAtUtc.Value
            : DateTime.UtcNow;

        user.Tier = tier;
        user.SubscriptionExpiresAtUtc = tier == SubscriptionTier.Free ? null : start.AddMonths(months);

        await _db.SaveChangesAsync(ct);
        return true;
    }

    private static void ResetIfNewDay(ApplicationUser user)
    {
        var today = DateTime.UtcNow.Date;
        if (user.QuotaResetAtUtc.Date < today)
        {
            user.DailyPredictionViews = 0;
            user.QuotaResetAtUtc = today;
        }
    }
}
