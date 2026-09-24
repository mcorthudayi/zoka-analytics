using Microsoft.EntityFrameworkCore;
using ZokaAnalytics.Data;
using ZokaAnalytics.Models;

namespace ZokaAnalytics.Services.Analysis;

public class FormService : IFormService
{
    private readonly ApplicationDbContext _db;

    public FormService(ApplicationDbContext db) => _db = db;

    public async Task<TeamFormResult?> GetFormAsync(int teamId, int lastN = 5, CancellationToken ct = default)
    {
        var team = await _db.Teams.AsNoTracking().FirstOrDefaultAsync(t => t.Id == teamId, ct);
        if (team is null) return null;

        var matches = await _db.Matches
            .Include(m => m.HomeTeam)
            .Include(m => m.AwayTeam)
            .Where(m => (m.HomeTeamId == teamId || m.AwayTeamId == teamId)
                        && m.Status == MatchStatus.Finished
                        && m.HomeGoals != null && m.AwayGoals != null)
            .OrderByDescending(m => m.KickoffUtc)
            .Take(lastN)
            .AsNoTracking()
            .ToListAsync(ct);

        if (matches.Count == 0)
            return new TeamFormResult(teamId, team.Name, string.Empty, 50, 0, 0, 0, 0, 0, 0, 0, Array.Empty<FormMatch>());

        matches.Reverse();

        var formMatches = new List<FormMatch>();
        int wins = 0, draws = 0, losses = 0, cleanSheets = 0, btts = 0;
        int goalsFor = 0, goalsAgainst = 0;

        foreach (var m in matches)
        {
            var isHome = m.HomeTeamId == teamId;
            var gf = isHome ? m.HomeGoals!.Value : m.AwayGoals!.Value;
            var ga = isHome ? m.AwayGoals!.Value : m.HomeGoals!.Value;

            var result = gf > ga ? 'W' : gf < ga ? 'L' : 'D';
            if (result == 'W') wins++;
            else if (result == 'D') draws++;
            else losses++;

            if (ga == 0) cleanSheets++;
            if (gf > 0 && ga > 0) btts++;

            goalsFor += gf;
            goalsAgainst += ga;

            formMatches.Add(new FormMatch(
                m.Id,
                m.KickoffUtc,
                isHome ? m.AwayTeam.Name : m.HomeTeam.Name,
                isHome,
                gf,
                ga,
                result));
        }

        double weightedPoints = 0, weightSum = 0;
        for (var i = 0; i < formMatches.Count; i++)
        {
            var weight = i + 1;
            var points = formMatches[i].Result switch { 'W' => 3.0, 'D' => 1.0, _ => 0.0 };
            weightedPoints += points * weight;
            weightSum += 3.0 * weight;
        }

        var formScore = weightSum > 0 ? weightedPoints / weightSum * 100 : 50;

        return new TeamFormResult(
            teamId,
            team.Name,
            new string(formMatches.Select(f => f.Result).ToArray()),
            Math.Round(formScore, 1),
            wins,
            draws,
            losses,
            Math.Round((double)goalsFor / formMatches.Count, 2),
            Math.Round((double)goalsAgainst / formMatches.Count, 2),
            cleanSheets,
            btts,
            formMatches.AsReadOnly());
    }
}
