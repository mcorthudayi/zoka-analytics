using System.ComponentModel.DataAnnotations;

namespace ZokaAnalytics.Models;

public class Coupon
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;

    [MaxLength(150)]
    public string Title { get; set; } = "Kuponum";

    public CouponStatus Status { get; set; } = CouponStatus.Open;

    public decimal Stake { get; set; }
    public decimal TotalOdds { get; set; }
    public decimal PotentialReturn { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? SettledAtUtc { get; set; }

    public ICollection<CouponItem> Items { get; set; } = new List<CouponItem>();
}