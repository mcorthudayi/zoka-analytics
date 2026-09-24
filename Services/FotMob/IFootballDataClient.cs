namespace ZokaAnalytics.Services.FotMob;

public interface IFootballDataClient
{
    int CallsUsed { get; }
    bool BudgetExceeded { get; }

    Task<FmLeagueDetail?> GetLeagueDetailAsync(int leagueId, CancellationToken ct = default);
    Task<List<FmMatch>> GetLeagueMatchesAsync(int leagueId, CancellationToken ct = default);
    Task<List<FmMatch>> GetMatchesByDateAsync(DateOnly date, CancellationToken ct = default);
    Task<List<FmMatch>> GetLiveMatchesAsync(CancellationToken ct = default);
}
