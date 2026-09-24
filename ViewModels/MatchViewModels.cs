using ZokaAnalytics.Models;
using ZokaAnalytics.Services.Analysis;

namespace ZokaAnalytics.ViewModels;

public class MatchListItemViewModel
{
    public int MatchId { get; set; }
    public DateTime KickoffUtc { get; set; }
    public MatchStatus Status { get; set; }
    public int? ElapsedMinutes { get; set; }

    public string LeagueName { get; set; } = string.Empty;
    public string? LeagueLogoUrl { get; set; }

    public string HomeTeamName { get; set; } = string.Empty;
    public string? HomeTeamLogoUrl { get; set; }
    public string AwayTeamName { get; set; } = string.Empty;
    public string? AwayTeamLogoUrl { get; set; }

    public int? HomeGoals { get; set; }
    public int? AwayGoals { get; set; }

    public bool IsFavorite { get; set; }
    public bool HasPrediction { get; set; }
    public MatchOutcome? PredictedOutcome { get; set; }
    public double? Confidence { get; set; }
    public double? HomeWinProbability { get; set; }
    public double? DrawProbability { get; set; }
    public double? AwayWinProbability { get; set; }

    public string KickoffLocal => KickoffUtc.ToLocalTime().ToString("dd.MM HH:mm");

    public string ScoreText => HomeGoals.HasValue && AwayGoals.HasValue
        ? $"{HomeGoals} - {AwayGoals}"
        : "-";

    public string StateLabel => Status switch
    {
        MatchStatus.Scheduled => "Bekliyor",
        MatchStatus.Live => ElapsedMinutes.HasValue ? $"{ElapsedMinutes}'" : "CANLI",
        MatchStatus.HalfTime => "Devre",
        MatchStatus.Finished => "Bitti",
        MatchStatus.Postponed => "Ertelendi",
        MatchStatus.Cancelled => "Iptal",
        MatchStatus.Abandoned => "Tatil",
        _ => "-"
    };

    public string StatusText => Status switch
    {
        MatchStatus.Scheduled => KickoffLocal,
        MatchStatus.Live => ElapsedMinutes.HasValue ? $"{ElapsedMinutes}'" : "CANLI",
        MatchStatus.HalfTime => "İY",
        MatchStatus.Finished => "MS",
        MatchStatus.Postponed => "Ertelendi",
        MatchStatus.Cancelled => "İptal",
        MatchStatus.Abandoned => "Tatil",
        _ => "-"
    };
}

public class MatchListViewModel
{
    public List<MatchListItemViewModel> Matches { get; set; } = new();
    public List<LeagueFilterItem> Leagues { get; set; } = new();
    public int? SelectedLeagueId { get; set; }
    public DateTime SelectedDate { get; set; } = DateTime.UtcNow.Date;
    public string Mode { get; set; } = "upcoming";
}

public class LeagueFilterItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public int MatchCount { get; set; }
}

public class MatchDetailViewModel
{
    public MatchListItemViewModel Match { get; set; } = new();

    public bool PredictionLocked { get; set; }
    public string? LockReason { get; set; }

    public Models.Prediction? Prediction { get; set; }
    public TeamStrengthResult? HomeStrength { get; set; }
    public TeamStrengthResult? AwayStrength { get; set; }
    public TeamFormResult? HomeForm { get; set; }
    public TeamFormResult? AwayForm { get; set; }
    public H2HResult? H2H { get; set; }

    public List<ZokaAnalytics.Services.Markets.ScoreLine> TopScores { get; set; } = new();
    public List<(string Label, double Probability)> GoalMarkets { get; set; } = new();

    public int QuotaUsed { get; set; }
    public int QuotaLimit { get; set; }
    public bool QuotaUnlimited => QuotaLimit == 0;
}