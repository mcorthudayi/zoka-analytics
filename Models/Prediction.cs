using System.ComponentModel.DataAnnotations;

namespace ZokaAnalytics.Models;

public class Prediction
{
    public int Id { get; set; }

    public int MatchId { get; set; }
    public Match Match { get; set; } = null!;

    public double HomeWinProbability { get; set; }
    public double DrawProbability { get; set; }
    public double AwayWinProbability { get; set; }

    public double Over25Probability { get; set; }
    public double BttsProbability { get; set; }

    public double ExpectedHomeGoals { get; set; }
    public double ExpectedAwayGoals { get; set; }

    public MatchOutcome PredictedOutcome { get; set; }

    public double Confidence { get; set; }

    [MaxLength(50)]
    public string ModelVersion { get; set; } = "v1";

    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;

    public bool IsSettled { get; set; }
    public bool? WasCorrect { get; set; }
}