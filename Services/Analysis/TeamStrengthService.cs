using Microsoft.EntityFrameworkCore;
using ZokaAnalytics.Data;
using ZokaAnalytics.Models;

namespace ZokaAnalytics.Services.Analysis;

public class TeamStrengthService : ITeamStrengthService
{
    private readonly ApplicationDbContext _db;

    public TeamStrengthService(ApplicationDbContext db) => _db = db;

    public async Task<TeamStrengthResult?> GetStrengthAsync(int teamId, int leagueId, int season, CancellationToken ct = default)
    {
        var all = await GetLeagueStrengthsAsync(leagueId, season, ct);
        var direct = all.FirstOrDefault(s => s.TeamId == teamId);
        if (direct is not null) return direct;

        var fallbackLeague = await _db.TeamStatistics
            .Where(x => x.TeamId == teamId && x.PlayedTotal > 0)
            .OrderByDescending(x => x.Season)
            .ThenByDescending(x => x.PlayedTotal)
            .Select(x => new { x.LeagueId, x.Season })
            .FirstOrDefaultAsync(ct);

        if (fallbackLeague is null) return null;

        var fallbackTable = await GetLeagueStrengthsAsync(fallbackLeague.LeagueId, fallbackLeague.Season, ct);
        return fallbackTable.FirstOrDefault(s => s.TeamId == teamId);
    }

    public async Task<IReadOnlyList<TeamStrengthResult>> GetLeagueStrengthsAsync(int leagueId, int season, CancellationToken ct = default)
    {
        var stats = await _db.TeamStatistics
            .Include(s => s.Team)
            .Where(s => s.LeagueId == leagueId && s.Season == season && s.PlayedTotal > 0)
            .AsNoTracking()
            .ToListAsync(ct);

        if (stats.Count == 0) return Array.Empty<TeamStrengthResult>();

        var leagueAvgGoalsFor = stats.Average(s => (double)s.GoalsForTotal / s.PlayedTotal);
        var leagueAvgGoalsAgainst = stats.Average(s => (double)s.GoalsAgainstTotal / s.PlayedTotal);
        var maxPpg = stats.Max(s => (double)s.Points / s.PlayedTotal);

        var results = new List<TeamStrengthResult>();

        foreach (var s in stats)
        {
            var played = s.PlayedTotal;
            var gf = (double)s.GoalsForTotal / played;
            var ga = (double)s.GoalsAgainstTotal / played;
            var ppg = (double)s.Points / played;

            var attackRatio = leagueAvgGoalsFor > 0 ? gf / leagueAvgGoalsFor : 1;
            var attack = Clamp(attackRatio * 50);

            var defenceRatio = ga > 0 && leagueAvgGoalsAgainst > 0 ? leagueAvgGoalsAgainst / ga : 2;
            var defence = Clamp(defenceRatio * 50);

            var homePpg = s.PlayedHome > 0 ? (s.WinsHome * 3.0 + s.DrawsHome) / s.PlayedHome : 0;
            var awayPpg = s.PlayedAway > 0 ? (s.WinsAway * 3.0 + s.DrawsAway) / s.PlayedAway : 0;

            var home = Clamp(homePpg / 3.0 * 100);
            var away = Clamp(awayPpg / 3.0 * 100);

            var pointsScore = maxPpg > 0 ? ppg / maxPpg * 100 : 0;
            var overall = Clamp(pointsScore * 0.55 + attack * 0.225 + defence * 0.225);

            results.Add(new TeamStrengthResult(
                s.TeamId,
                s.Team?.Name ?? $"#{s.TeamId}",
                Math.Round(overall, 1),
                Math.Round(attack, 1),
                Math.Round(defence, 1),
                Math.Round(home, 1),
                Math.Round(away, 1),
                Math.Round(ppg, 2),
                Math.Round(gf, 2),
                Math.Round(ga, 2),
                played));
        }

        return results.OrderByDescending(r => r.OverallStrength).ToList();
    }

    public async Task<int> RecalculateLeagueAsync(int leagueId, int season, CancellationToken ct = default)
    {
        var strengths = await GetLeagueStrengthsAsync(leagueId, season, ct);
        if (strengths.Count == 0) return 0;

        var map = strengths.ToDictionary(s => s.TeamId, s => s.OverallStrength);
        var teamIds = map.Keys.ToList();

        var stats = await _db.TeamStatistics
            .Where(s => s.LeagueId == leagueId && s.Season == season && teamIds.Contains(s.TeamId))
            .ToListAsync(ct);

        foreach (var stat in stats)
        {
            if (map.TryGetValue(stat.TeamId, out var value))
                stat.StrengthScore = value;
        }

        await _db.SaveChangesAsync(ct);
        return stats.Count;
    }

    private static double Clamp(double value) => Math.Clamp(value, 0, 100);
}
