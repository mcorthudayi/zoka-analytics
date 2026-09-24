using System.ComponentModel.DataAnnotations;

namespace ZokaAnalytics.Models;

public class CouponItem
{
    public int Id { get; set; }

    public int CouponId { get; set; }
    public Coupon Coupon { get; set; } = null!;

    public int MatchId { get; set; }
    public Match Match { get; set; } = null!;

    public BetType Selection { get; set; }

    [MaxLength(80)]
    public string SelectionLabel { get; set; } = string.Empty;

    public decimal Odds { get; set; } = 1.00m;

    public SelectionStatus Status { get; set; } = SelectionStatus.Pending;
}