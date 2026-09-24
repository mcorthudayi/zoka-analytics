using Microsoft.ML.Data;

namespace ZokaAnalytics.Services.Prediction;

public class MatchFeatures
{
    public float HomeStrength { get; set; }
    public float HomeAttack { get; set; }
    public float HomeDefence { get; set; }
    public float HomeVenueStrength { get; set; }
    public float HomeFormScore { get; set; }
    public float HomeAvgGoalsFor { get; set; }
    public float HomeAvgGoalsAgainst { get; set; }
    public float HomePointsPerGame { get; set; }

    public float AwayStrength { get; set; }
    public float AwayAttack { get; set; }
    public float AwayDefence { get; set; }
    public float AwayVenueStrength { get; set; }
    public float AwayFormScore { get; set; }
    public float AwayAvgGoalsFor { get; set; }
    public float AwayAvgGoalsAgainst { get; set; }
    public float AwayPointsPerGame { get; set; }

    public float HomeRestDays { get; set; }
    public float AwayRestDays { get; set; }
    public float RestDiff { get; set; }

    public float HomeCongestion { get; set; }
    public float AwayCongestion { get; set; }

    public float HomeRank { get; set; }
    public float AwayRank { get; set; }

    public float HomePointsBehindLeader { get; set; }
    public float AwayPointsBehindLeader { get; set; }

    public float HomePointsAboveDrop { get; set; }
    public float AwayPointsAboveDrop { get; set; }

    public float SeasonProgress { get; set; }

    public float StrengthDiff { get; set; }
    public float FormDiff { get; set; }

    public float H2HMatches { get; set; }
    public float H2HHomeWinRate { get; set; }
    public float H2HDrawRate { get; set; }
    public float H2HAvgGoals { get; set; }
    public float H2HOver25Rate { get; set; }
    public float H2HBttsRate { get; set; }

    public string Label { get; set; } = string.Empty;
}

public class MatchPredictionOutput
{
    [ColumnName("PredictedLabel")]
    public string PredictedLabel { get; set; } = string.Empty;

    public float[] Score { get; set; } = Array.Empty<float>();
}
