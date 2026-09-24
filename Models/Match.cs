using System.ComponentModel.DataAnnotations;

namespace ZokaAnalytics.Models;

public class Match
{
    public int Id { get; set; }

    public int LeagueId { get; set; }
    public League League { get; set; } = null!;

    public int Season { get; set; }

    [MaxLength(120)]
    public string? Round { get; set; }

    public int HomeTeamId { get; set; }
    public Team HomeTeam { get; set; } = null!;

    public int AwayTeamId { get; set; }
    public Team AwayTeam { get; set; } = null!;

    public DateTime KickoffUtc { get; set; }

    public MatchStatus Status { get; set; } = MatchStatus.Scheduled;

    public int? ElapsedMinutes { get; set; }

    public int? HomeGoals { get; set; }
    public int? AwayGoals { get; set; }
    public int? HomeHalftimeGoals { get; set; }
    public int? AwayHalftimeGoals { get; set; }

    [MaxLength(200)]
    public string? VenueName { get; set; }

    public decimal? OddsHome { get; set; }
    public decimal? OddsDraw { get; set; }
    public decimal? OddsAway { get; set; }

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public Prediction? Prediction { get; set; }
    public ICollection<CouponItem> CouponItems { get; set; } = new List<CouponItem>();

    public bool IsFinished => Status == MatchStatus.Finished;

    public MatchOutcome? Outcome =>
        !IsFinished || HomeGoals is null || AwayGoals is null
            ? null
            : HomeGoals > AwayGoals ? MatchOutcome.HomeWin
            : HomeGoals < AwayGoals ? MatchOutcome.AwayWin
            : MatchOutcome.Draw;

    public int? TotalGoals => HomeGoals is null || AwayGoals is null ? null : HomeGoals + AwayGoals;
}