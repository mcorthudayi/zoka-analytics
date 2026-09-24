namespace ZokaAnalytics.Services.Analysis;

public record H2HMatchSummary(
    int MatchId,
    DateTime KickoffUtc,
    string HomeTeamName,
    string AwayTeamName,
    int HomeGoals,
    int AwayGoals,
    string? LeagueName);

public record H2HResult(
    int HomeTeamId,
    int AwayTeamId,
    string HomeTeamName,
    string AwayTeamName,
    int TotalMatches,
    int HomeTeamWins,
    int Draws,
    int AwayTeamWins,
    double AvgTotalGoals,
    double Over25Rate,
    double BttsRate,
    DateTime? LastMeetingUtc,
    IReadOnlyList<H2HMatchSummary> RecentMeetings);

public interface IH2HService
{
    Task<H2HResult> GetH2HAsync(int homeTeamId, int awayTeamId, int lastN = 10, CancellationToken ct = default);
}
