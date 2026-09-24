using System.ComponentModel.DataAnnotations;

namespace ZokaAnalytics.Models;

public class League
{
    public int Id { get; set; }

    [MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(100)]
    public string Country { get; set; } = string.Empty;

    [MaxLength(400)]
    public string? LogoUrl { get; set; }

    [MaxLength(400)]
    public string? FlagUrl { get; set; }

    public int CurrentSeason { get; set; }

    public bool IsTracked { get; set; }

    public int DisplayOrder { get; set; }

    public DateTime? LastSyncedAtUtc { get; set; }

    public ICollection<Match> Matches { get; set; } = new List<Match>();
    public ICollection<TeamStatistic> TeamStatistics { get; set; } = new List<TeamStatistic>();
}