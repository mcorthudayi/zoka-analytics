using System.ComponentModel.DataAnnotations;

namespace ZokaAnalytics.Models;

public class MatchContext
{
    public int Id { get; set; }

    public int MatchId { get; set; }
    public Match Match { get; set; } = null!;

    public int HomeKeyPlayersMissing { get; set; }
    public int AwayKeyPlayersMissing { get; set; }

    public bool HomeCoachChanged { get; set; }
    public bool AwayCoachChanged { get; set; }

    public bool IsDerby { get; set; }

    public bool HomeNothingToPlayFor { get; set; }
    public bool AwayNothingToPlayFor { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    private const double PerPlayerPenalty = 0.09;
    private const double CoachChangeBounce = 0.03;
    private const double ApathyPenalty = 0.07;

    public double HomeXgMultiplier => Multiplier(HomeKeyPlayersMissing, HomeCoachChanged, HomeNothingToPlayFor);
    public double AwayXgMultiplier => Multiplier(AwayKeyPlayersMissing, AwayCoachChanged, AwayNothingToPlayFor);

    private static double Multiplier(int missing, bool coachChanged, bool apathy)
    {
        var value = 1.0;
        value *= Math.Pow(1 - PerPlayerPenalty, Math.Clamp(missing, 0, 5));
        if (coachChanged) value *= 1 + CoachChangeBounce;
        if (apathy) value *= 1 - ApathyPenalty;
        return Math.Clamp(value, 0.55, 1.15);
    }

    public bool HasAnyAdjustment =>
        HomeKeyPlayersMissing > 0 || AwayKeyPlayersMissing > 0
        || HomeCoachChanged || AwayCoachChanged
        || IsDerby || HomeNothingToPlayFor || AwayNothingToPlayFor;

    public string Summary()
    {
        var parts = new List<string>();

        if (HomeKeyPlayersMissing > 0) parts.Add($"Ev sahibinde {HomeKeyPlayersMissing} önemli eksik");
        if (AwayKeyPlayersMissing > 0) parts.Add($"Deplasmanda {AwayKeyPlayersMissing} önemli eksik");
        if (HomeCoachChanged) parts.Add("Ev sahibinde teknik direktör değişimi");
        if (AwayCoachChanged) parts.Add("Deplasmanda teknik direktör değişimi");
        if (IsDerby) parts.Add("Derbi");
        if (HomeNothingToPlayFor) parts.Add("Ev sahibinin oynayacağı bir şey yok");
        if (AwayNothingToPlayFor) parts.Add("Deplasmanın oynayacağı bir şey yok");

        return parts.Count > 0 ? string.Join(" · ", parts) : "Düzeltme yok";
    }
}
