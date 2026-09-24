namespace ZokaAnalytics.Models;

public class H2HRecord
{
    public int Id { get; set; }

    public int TeamAId { get; set; }
    public Team TeamA { get; set; } = null!;

    public int TeamBId { get; set; }
    public Team TeamB { get; set; } = null!;

    public int TotalMatches { get; set; }
    public int TeamAWins { get; set; }
    public int Draws { get; set; }
    public int TeamBWins { get; set; }

    public int TeamAGoals { get; set; }
    public int TeamBGoals { get; set; }

    public double AvgTotalGoals { get; set; }
    public double Over25Rate { get; set; }
    public double BttsRate { get; set; }

    public DateTime? LastMeetingUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}