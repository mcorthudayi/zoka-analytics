using ZokaAnalytics.Models;

namespace ZokaAnalytics.ViewModels;

public class CouponBuilderViewModel
{
    public List<MatchListItemViewModel> AvailableMatches { get; set; } = new();
    public List<SuggestionItem> Suggestions { get; set; } = new();
    public List<Coupon> MyCoupons { get; set; } = new();

    public int OpenCount => MyCoupons.Count(c => c.Status == CouponStatus.Open);
    public int WonCount => MyCoupons.Count(c => c.Status == CouponStatus.Won);
    public int LostCount => MyCoupons.Count(c => c.Status == CouponStatus.Lost);

    public double WinRate
    {
        get
        {
            var settled = WonCount + LostCount;
            return settled > 0 ? Math.Round((double)WonCount / settled * 100, 1) : 0;
        }
    }
}

public class SuggestionItem
{
    public int MatchId { get; set; }
    public string HomeTeamName { get; set; } = string.Empty;
    public string AwayTeamName { get; set; } = string.Empty;
    public DateTime KickoffUtc { get; set; }
    public string LeagueName { get; set; } = string.Empty;
    public BetType Selection { get; set; }
    public string Label { get; set; } = string.Empty;
    public decimal Odds { get; set; }
    public double Probability { get; set; }
}

public class AdminDashboardViewModel
{
    public int UserCount { get; set; }
    public int MatchCount { get; set; }
    public int PredictionCount { get; set; }
    public int SettledPredictions { get; set; }
    public double Accuracy { get; set; }
    public int CouponCount { get; set; }
    public int TeamCount { get; set; }
    public bool ModelLoaded { get; set; }

    public List<ApplicationUser> RecentUsers { get; set; } = new();
    public List<SyncLog> RecentSyncs { get; set; } = new();
    public List<LeagueStat> LeagueStats { get; set; } = new();
}

public class LeagueStat
{
    public string Name { get; set; } = string.Empty;
    public int Matches { get; set; }
    public int Predictions { get; set; }
    public DateTime? LastSyncedAtUtc { get; set; }
}
