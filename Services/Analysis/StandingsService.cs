using Microsoft.EntityFrameworkCore;
using ZokaAnalytics.Data;

namespace ZokaAnalytics.Services.Analysis;

public class StandingsService : IStandingsService
{
    private readonly ApplicationDbContext _db;

    public StandingsService(ApplicationDbContext db) => _db = db;

    public Task<List<int>> GetAvailableSeasonsAsync(int leagueId, CancellationToken ct = default)
        => _db.TeamStatistics
            .Where(s => s.LeagueId == leagueId && s.PlayedTotal > 0)
            .Select(s => s.Season)
            .Distinct()
            .OrderByDescending(x => x)
            .ToListAsync(ct);

    public async Task<StandingsTable?> GetAsync(
        int leagueId, int? season = null, string scope = "all", CancellationToken ct = default)
    {
        var league = await _db.Leagues.AsNoTracking().FirstOrDefaultAsync(l => l.Id == leagueId, ct);
        if (league is null) return null;

        var targetSeason = season ?? league.CurrentSeason;

        var stats = await _db.TeamStatistics
            .Include(s => s.Team)
            .Where(s => s.LeagueId == leagueId && s.Season == targetSeason && s.PlayedTotal > 0)
            .AsNoTracking()
            .ToListAsync(ct);

        if (stats.Count == 0) return null;

        var rows = stats.Select(s =>
        {
            var (played, wins, draws, losses, gf, ga) = scope switch
            {
                "home" => (s.PlayedHome, s.WinsHome, s.DrawsHome, s.LossesHome, s.GoalsForHome, s.GoalsAgainstHome),
                "away" => (s.PlayedAway, s.WinsAway, s.DrawsAway, s.LossesAway, s.GoalsForAway, s.GoalsAgainstAway),
                _ => (s.PlayedTotal, s.WinsTotal, s.DrawsTotal, s.LossesTotal, s.GoalsForTotal, s.GoalsAgainstTotal)
            };

            return new StandingRow(
                Rank: 0,
                TeamId: s.TeamId,
                TeamName: s.Team?.Name ?? $"#{s.TeamId}",
                LogoUrl: s.Team?.LogoUrl,
                Played: played,
                Wins: wins,
                Draws: draws,
                Losses: losses,
                GoalsFor: gf,
                GoalsAgainst: ga,
                Points: wins * 3 + draws,
                Form: s.Form,
                Strength: s.StrengthScore);
        })
        .Where(r => r.Played > 0)
        .OrderByDescending(r => r.Points)
        .ThenByDescending(r => r.GoalDifference)
        .ThenByDescending(r => r.GoalsFor)
        .ThenBy(r => r.TeamName)
        .ToList();

        var ranked = rows.Select((r, i) => r with { Rank = i + 1 }).ToList();

        return new StandingsTable(
            league.Id,
            league.Name,
            league.LogoUrl,
            targetSeason,
            ranked,
            stats.Max(s => s.UpdatedAtUtc));
    }
}
