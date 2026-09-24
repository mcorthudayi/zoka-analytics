using ZokaAnalytics.Models;

namespace ZokaAnalytics.Services.Coupons;

public record CouponResult(bool Success, int? CouponId, string? Error);

public record SelectionInput(int MatchId, BetType Selection);

public interface ICouponService
{
    Task<CouponResult> CreateAsync(string userId, string title, decimal stake, List<SelectionInput> picks, CancellationToken ct = default);
    Task<List<Coupon>> GetUserCouponsAsync(string userId, CancellationToken ct = default);
    Task<Coupon?> GetAsync(int couponId, string userId, CancellationToken ct = default);
    Task<bool> DeleteAsync(int couponId, string userId, CancellationToken ct = default);
    Task<int> SettleAllAsync(CancellationToken ct = default);
    Task<List<(int MatchId, BetType Selection, string Label, decimal Odds, double Probability)>> BuildSuggestionsAsync(int count, CancellationToken ct = default);
}
