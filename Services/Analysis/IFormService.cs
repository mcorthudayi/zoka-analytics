namespace ZokaAnalytics.Services.Analysis;

public record FormMatch(
    int MatchId,
    DateTime KickoffUtc,
    string OpponentName,
    bool WasHome,
    int GoalsFor,
    int GoalsAgainst,
    char Result);

public record TeamFormResult(
    int TeamId,
    string TeamName,
    string FormString,
    double FormScore,
    int Wins,
    int Draws,
    int Losses,
    double AvgGoalsFor,
    double AvgGoalsAgainst,
    int CleanSheets,
    int BttsCount,
    IReadOnlyList<FormMatch> RecentMatches);

public interface IFormService
{
    Task<TeamFormResult?> GetFormAsync(int teamId, int lastN = 5, CancellationToken ct = default);
}
