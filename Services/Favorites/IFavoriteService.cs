namespace ZokaAnalytics.Services.Favorites;

public interface IFavoriteService
{
    Task<HashSet<int>> GetMatchIdsAsync(string userId, CancellationToken ct = default);
    Task<HashSet<int>> GetTeamIdsAsync(string userId, CancellationToken ct = default);
    Task<bool> ToggleMatchAsync(string userId, int matchId, CancellationToken ct = default);
    Task<bool> ToggleTeamAsync(string userId, int teamId, CancellationToken ct = default);
    Task<int> CountAsync(string userId, CancellationToken ct = default);
}
