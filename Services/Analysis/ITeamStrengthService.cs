namespace ZokaAnalytics.Services.Analysis;

public record TeamStrengthResult(
    int TeamId,
    string TeamName,
    double OverallStrength,
    double AttackStrength,
    double DefenceStrength,
    double HomeStrength,
    double AwayStrength,
    double PointsPerGame,
    double AvgGoalsFor,
    double AvgGoalsAgainst,
    int MatchesPlayed);

public interface ITeamStrengthService
{
    Task<TeamStrengthResult?> GetStrengthAsync(int teamId, int leagueId, int season, CancellationToken ct = default);
    Task<IReadOnlyList<TeamStrengthResult>> GetLeagueStrengthsAsync(int leagueId, int season, CancellationToken ct = default);
    Task<int> RecalculateLeagueAsync(int leagueId, int season, CancellationToken ct = default);
}
