using System.ComponentModel.DataAnnotations;

namespace ZokaAnalytics.Models;

public class Team
{
    public int Id { get; set; }

    [MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Code { get; set; }

    [MaxLength(100)]
    public string? Country { get; set; }

    [MaxLength(400)]
    public string? LogoUrl { get; set; }

    public int? Founded { get; set; }

    [MaxLength(200)]
    public string? VenueName { get; set; }

    [MaxLength(100)]
    public string? VenueCity { get; set; }

    public DateTime? LastSyncedAtUtc { get; set; }

    public ICollection<Match> HomeMatches { get; set; } = new List<Match>();
    public ICollection<Match> AwayMatches { get; set; } = new List<Match>();
    public ICollection<TeamStatistic> Statistics { get; set; } = new List<TeamStatistic>();
}