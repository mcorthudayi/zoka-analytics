using Microsoft.AspNetCore.Identity;

namespace ZokaAnalytics.Models;

public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;

    public SubscriptionTier Tier { get; set; } = SubscriptionTier.Free;

    public DateTime? SubscriptionExpiresAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAtUtc { get; set; }

    public int DailyPredictionViews { get; set; }
    public DateTime QuotaResetAtUtc { get; set; } = DateTime.UtcNow.Date;

    public bool IsBanned { get; set; }

    public ICollection<Coupon> Coupons { get; set; } = new List<Coupon>();

    public SubscriptionTier EffectiveTier =>
        Tier == SubscriptionTier.Free
            ? SubscriptionTier.Free
            : (SubscriptionExpiresAtUtc.HasValue && SubscriptionExpiresAtUtc.Value < DateTime.UtcNow
                ? SubscriptionTier.Free
                : Tier);
}