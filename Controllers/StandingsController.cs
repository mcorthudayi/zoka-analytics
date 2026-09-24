using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ZokaAnalytics.Data;
using ZokaAnalytics.Services.Analysis;
using ZokaAnalytics.ViewModels;

namespace ZokaAnalytics.Controllers;

public class StandingsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IStandingsService _standings;

    public StandingsController(ApplicationDbContext db, IStandingsService standings)
    {
        _db = db;
        _standings = standings;
    }

    public async Task<IActionResult> Index(int? leagueId, int? season, string scope = "all", CancellationToken ct = default)
    {
        var leagues = await _db.Leagues
            .Where(l => l.IsTracked)
            .OrderBy(l => l.DisplayOrder)
            .Select(l => new LeagueFilterItem { Id = l.Id, Name = l.Name, LogoUrl = l.LogoUrl })
            .AsNoTracking()
            .ToListAsync(ct);

        var selected = leagueId ?? leagues.FirstOrDefault()?.Id;
        StandingsTable? table = null;

        var seasons = new List<int>();

        if (selected.HasValue)
        {
            seasons = await _standings.GetAvailableSeasonsAsync(selected.Value, ct);
            table = await _standings.GetAsync(selected.Value, season, scope, ct);

            if (table is null && season is null && seasons.Count > 0)
                table = await _standings.GetAsync(selected.Value, seasons[0], scope, ct);
        }

        ViewBag.Seasons = seasons;
        ViewBag.SelectedSeason = table?.Season ?? season;
        ViewBag.Leagues = leagues;
        ViewBag.SelectedLeagueId = selected;
        ViewBag.Scope = scope;

        return View(table);
    }
}
