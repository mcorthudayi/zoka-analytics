using ZokaAnalytics.Models;

namespace ZokaAnalytics.Services.Prediction;

public record QuotaCheckResult(bool Allowed, int Used, int Limit, string? Reason)
{
    public bool IsUnlimited => Limit == 0;
    public int Remaining => IsUnlimited ? int.MaxValue : Math.Max(0, Limit - Used);
}

public interface ISubscriptionService
{
    int GetDailyLimit(SubscriptionTier tier);
    Task<QuotaCheckResult> CheckQuotaAsync(string userId, CancellationToken ct = default);
    Task<QuotaCheckResult> ConsumeQuotaAsync(string userId, CancellationToken ct = default);
    Task<bool> UpgradeAsync(string userId, SubscriptionTier tier, int months, CancellationToken ct = default);
}
