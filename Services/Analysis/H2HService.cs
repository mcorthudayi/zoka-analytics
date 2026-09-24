using Microsoft.EntityFrameworkCore;
using ZokaAnalytics.Data;
using ZokaAnalytics.Models;

namespace ZokaAnalytics.Services.Analysis;

public class H2HService : IH2HService
{
    private readonly ApplicationDbContext _db;

    public H2HService(ApplicationDbContext db) => _db = db;

    public async Task<H2HResult> GetH2HAsync(int homeTeamId, int awayTeamId, int lastN = 10, CancellationToken ct = default)
    {
        var teams = await _db.Teams
            .Where(t => t.Id == homeTeamId || t.Id == awayTeamId)
            .AsNoTracking()
            .ToDictionaryAsync(t => t.Id, t => t.Name, ct);

        var homeName = teams.GetValueOrDefault(homeTeamId, $"#{homeTeamId}");
        var awayName = teams.GetValueOrDefault(awayTeamId, $"#{awayTeamId}");

        var matches = await _db.Matches
            .Include(m => m.HomeTeam)
            .Include(m => m.AwayTeam)
            .Include(m => m.League)
            .Where(m => m.Status == MatchStatus.Finished
                        && m.HomeGoals != null && m.AwayGoals != null
                        && ((m.HomeTeamId == homeTeamId && m.AwayTeamId == awayTeamId)
                            || (m.HomeTeamId == awayTeamId && m.AwayTeamId == homeTeamId)))
            .OrderByDescending(m => m.KickoffUtc)
            .Take(lastN)
            .AsNoTracking()
            .ToListAsync(ct);

        if (matches.Count == 0)
        {
            return new H2HResult(homeTeamId, awayTeamId, homeName, awayName,
                0, 0, 0, 0, 0, 0, 0, null, Array.Empty<H2HMatchSummary>());
        }

        int homeWins = 0, draws = 0, awayWins = 0, over25 = 0, btts = 0, totalGoals = 0;
        var summaries = new List<H2HMatchSummary>();

        foreach (var m in matches)
        {
            var hg = m.HomeGoals!.Value;
            var ag = m.AwayGoals!.Value;

            var subjectGoals = m.HomeTeamId == homeTeamId ? hg : ag;
            var opponentGoals = m.HomeTeamId == homeTeamId ? ag : hg;

            if (subjectGoals > opponentGoals) homeWins++;
            else if (subjectGoals < opponentGoals) awayWins++;
            else draws++;

            var sum = hg + ag;
            totalGoals += sum;
            if (sum > 2) over25++;
            if (hg > 0 && ag > 0) btts++;

            summaries.Add(new H2HMatchSummary(
                m.Id, m.KickoffUtc, m.HomeTeam.Name, m.AwayTeam.Name, hg, ag, m.League?.Name));
        }

        var count = matches.Count;

        return new H2HResult(
            homeTeamId,
            awayTeamId,
            homeName,
            awayName,
            count,
            homeWins,
            draws,
            awayWins,
            Math.Round((double)totalGoals / count, 2),
            Math.Round((double)over25 / count, 3),
            Math.Round((double)btts / count, 3),
            matches[0].KickoffUtc,
            summaries.AsReadOnly());
    }
}
