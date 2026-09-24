using System.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ZokaAnalytics.Data;
using ZokaAnalytics.Models;
using ZokaAnalytics.Services.Favorites;
using ZokaAnalytics.Services.Prediction;
using ZokaAnalytics.ViewModels;

namespace ZokaAnalytics.Controllers;

public class HomeController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IMlModelStore _modelStore;
    private readonly IFavoriteService _favorites;
    private readonly UserManager<ApplicationUser> _userManager;

    public HomeController(
        ApplicationDbContext db,
        IMlModelStore modelStore,
        IFavoriteService favorites,
        UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _modelStore = modelStore;
        _favorites = favorites;
        _userManager = userManager;
    }

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var todayStart = DateTime.UtcNow.Date;
        var todayEnd = todayStart.AddDays(1);

        var todayMatches = await BaseQuery()
            .Where(m => m.KickoffUtc >= todayStart && m.KickoffUtc < todayEnd)
            .OrderBy(m => m.KickoffUtc)
            .Take(20)
            .Select(MatchProjection())
            .ToListAsync(ct);

        var liveMatches = await BaseQuery()
            .Where(m => m.Status == MatchStatus.Live || m.Status == MatchStatus.HalfTime)
            .OrderBy(m => m.KickoffUtc)
            .Take(10)
            .Select(MatchProjection())
            .ToListAsync(ct);

        var topPredictions = await BaseQuery()
            .Where(m => m.KickoffUtc >= DateTime.UtcNow
                        && m.Status == MatchStatus.Scheduled
                        && m.Prediction != null)
            .OrderByDescending(m => m.Prediction!.Confidence)
            .Take(6)
            .Select(MatchProjection())
            .ToListAsync(ct);

        if (User.Identity?.IsAuthenticated == true)
        {
            var uid = _userManager.GetUserId(User)!;
            var favIds = await _favorites.GetMatchIdsAsync(uid, ct);

            foreach (var m in todayMatches.Concat(liveMatches).Concat(topPredictions))
                m.IsFavorite = favIds.Contains(m.MatchId);
        }

        var settled = await _db.Predictions.CountAsync(p => p.IsSettled, ct);
        var correct = await _db.Predictions.CountAsync(p => p.IsSettled && p.WasCorrect == true, ct);

        var model = new HomeViewModel
        {
            TodayMatches = todayMatches,
            LiveMatches = liveMatches,
            TopPredictions = topPredictions,
            TotalMatches = await _db.Matches.CountAsync(ct),
            TotalPredictions = await _db.Predictions.CountAsync(ct),
            SettledPredictions = settled,
            PredictionAccuracy = settled > 0 ? Math.Round((double)correct / settled * 100, 1) : 0,
            ModelTrained = _modelStore.IsLoaded
        };

        return View(model);
    }

    public IActionResult Privacy() => View();

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
        => View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });

    private IQueryable<Match> BaseQuery() => _db.Matches
        .Include(m => m.League)
        .Include(m => m.HomeTeam)
        .Include(m => m.AwayTeam)
        .Include(m => m.Prediction)
        .AsNoTracking();

    public static System.Linq.Expressions.Expression<Func<Match, MatchListItemViewModel>> MatchProjection()
        => m => new MatchListItemViewModel
        {
            MatchId = m.Id,
            KickoffUtc = m.KickoffUtc,
            Status = m.Status,
            ElapsedMinutes = m.ElapsedMinutes,
            LeagueName = m.League.Name,
            LeagueLogoUrl = m.League.LogoUrl,
            HomeTeamName = m.HomeTeam.Name,
            HomeTeamLogoUrl = m.HomeTeam.LogoUrl,
            AwayTeamName = m.AwayTeam.Name,
            AwayTeamLogoUrl = m.AwayTeam.LogoUrl,
            HomeGoals = m.HomeGoals,
            AwayGoals = m.AwayGoals,
            HasPrediction = m.Prediction != null,
            PredictedOutcome = m.Prediction != null ? m.Prediction.PredictedOutcome : null,
            Confidence = m.Prediction != null ? m.Prediction.Confidence : null,
            HomeWinProbability = m.Prediction != null ? m.Prediction.HomeWinProbability : null,
            DrawProbability = m.Prediction != null ? m.Prediction.DrawProbability : null,
            AwayWinProbability = m.Prediction != null ? m.Prediction.AwayWinProbability : null
        };
}