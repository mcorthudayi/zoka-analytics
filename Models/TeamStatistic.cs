using System.ComponentModel.DataAnnotations;

namespace ZokaAnalytics.Models;

public class TeamStatistic
{
    public int Id { get; set; }

    public int TeamId { get; set; }
    public Team Team { get; set; } = null!;

    public int LeagueId { get; set; }
    public League League { get; set; } = null!;

    public int Season { get; set; }

    public int PlayedTotal { get; set; }
    public int PlayedHome { get; set; }
    public int PlayedAway { get; set; }

    public int WinsTotal { get; set; }
    public int WinsHome { get; set; }
    public int WinsAway { get; set; }

    public int DrawsTotal { get; set; }
    public int DrawsHome { get; set; }
    public int DrawsAway { get; set; }

    public int LossesTotal { get; set; }
    public int LossesHome { get; set; }
    public int LossesAway { get; set; }

    public int GoalsForTotal { get; set; }
    public int GoalsForHome { get; set; }
    public int GoalsForAway { get; set; }

    public int GoalsAgainstTotal { get; set; }
    public int GoalsAgainstHome { get; set; }
    public int GoalsAgainstAway { get; set; }

    public int CleanSheets { get; set; }
    public int FailedToScore { get; set; }

    [MaxLength(20)]
    public string Form { get; set; } = string.Empty;

    public int Points { get; set; }
    public int? Rank { get; set; }

    public double AvgGoalsFor { get; set; }
    public double AvgGoalsAgainst { get; set; }

    public double StrengthScore { get; set; }

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}