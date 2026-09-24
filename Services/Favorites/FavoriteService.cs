using Microsoft.EntityFrameworkCore;
using ZokaAnalytics.Data;
using ZokaAnalytics.Models;

namespace ZokaAnalytics.Services.Favorites;

public class FavoriteService : IFavoriteService
{
    private readonly ApplicationDbContext _db;

    public FavoriteService(ApplicationDbContext db) => _db = db;

    public async Task<HashSet<int>> GetMatchIdsAsync(string userId, CancellationToken ct = default)
        => (await _db.FavoriteMatches
                .Where(f => f.UserId == userId)
                .Select(f => f.MatchId)
                .ToListAsync(ct))
            .ToHashSet();

    public async Task<HashSet<int>> GetTeamIdsAsync(string userId, CancellationToken ct = default)
        => (await _db.FavoriteTeams
                .Where(f => f.UserId == userId)
                .Select(f => f.TeamId)
                .ToListAsync(ct))
            .ToHashSet();

    public async Task<bool> ToggleMatchAsync(string userId, int matchId, CancellationToken ct = default)
    {
        var existing = await _db.FavoriteMatches
            .FirstOrDefaultAsync(f => f.UserId == userId && f.MatchId == matchId, ct);

        if (existing is not null)
        {
            _db.FavoriteMatches.Remove(existing);
            await _db.SaveChangesAsync(ct);
            return false;
        }

        if (!await _db.Matches.AnyAsync(m => m.Id == matchId, ct))
            return false;

        _db.FavoriteMatches.Add(new FavoriteMatch { UserId = userId, MatchId = matchId });
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> ToggleTeamAsync(string userId, int teamId, CancellationToken ct = default)
    {
        var existing = await _db.FavoriteTeams
            .FirstOrDefaultAsync(f => f.UserId == userId && f.TeamId == teamId, ct);

        if (existing is not null)
        {
            _db.FavoriteTeams.Remove(existing);
            await _db.SaveChangesAsync(ct);
            return false;
        }

        if (!await _db.Teams.AnyAsync(t => t.Id == teamId, ct))
            return false;

        _db.FavoriteTeams.Add(new FavoriteTeam { UserId = userId, TeamId = teamId });
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<int> CountAsync(string userId, CancellationToken ct = default)
    {
        var matches = await _db.FavoriteMatches.CountAsync(f => f.UserId == userId, ct);
        var teams = await _db.FavoriteTeams.CountAsync(f => f.UserId == userId, ct);
        return matches + teams;
    }
}
