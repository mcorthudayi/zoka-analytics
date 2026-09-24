using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ZokaAnalytics.Data;
using ZokaAnalytics.Models;
using ZokaAnalytics.Services.Analysis;
using ZokaAnalytics.Services.Favorites;
using ZokaAnalytics.Services.Markets;
using ZokaAnalytics.Services.Prediction;
using ZokaAnalytics.ViewModels;

namespace ZokaAnalytics.Controllers;

public class MatchesController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly ITeamStrengthService _strength;
    private readonly IFormService _form;
    private readonly IH2HService _h2h;
    private readonly IMatchPredictionEngine _engine;
    private readonly ISubscriptionService _subscription;
    private readonly IFavoriteService _favorites;
    private readonly UserManager<ApplicationUser> _userManager;

    public MatchesController(
        ApplicationDbContext db,
        ITeamStrengthService strength,
        IFormService form,
        IH2HService h2h,
        IMatchPredictionEngine engine,
        ISubscriptionService subscription,
        IFavoriteService favorites,
        UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _strength = strength;
        _form = form;
        _h2h = h2h;
        _engine = engine;
        _subscription = subscription;
        _favorites = favorites;
        _userManager = userManager;
    }

    public async Task<IActionResult> Index(
        string mode = "upcoming",
        int? leagueId = null,
        DateTime? date = null,
        CancellationToken ct = default)
    {
        var query = BaseQuery();

        HashSet<int> favoriteMatchIds = new();
        HashSet<int> favoriteTeamIds = new();

        if (User.Identity?.IsAuthenticated == true)
        {
            var uid = _userManager.GetUserId(User)!;
            favoriteMatchIds = await _favorites.GetMatchIdsAsync(uid, ct);
            favoriteTeamIds = await _favorites.GetTeamIdsAsync(uid, ct);
        }

        if (mode == "favorites")
        {
            query = query.Where(m => favoriteMatchIds.Contains(m.Id)
                                     || favoriteTeamIds.Contains(m.HomeTeamId)
                                     || favoriteTeamIds.Contains(m.AwayTeamId));
        }

        query = mode switch
        {
            "live" => query.Where(m => m.Status == MatchStatus.Live || m.Status == MatchStatus.HalfTime),
            "results" => query.Where(m => m.Status == MatchStatus.Finished),
            "favorites" => query,
            _ => query.Where(m => m.KickoffUtc >= DateTime.UtcNow && m.Status == MatchStatus.Scheduled)
        };

        if (leagueId.HasValue)
            query = query.Where(m => m.LeagueId == leagueId.Value);

        if (date.HasValue)
        {
            var start = date.Value.Date;
            var end = start.AddDays(1);
            query = query.Where(m => m.KickoffUtc >= start && m.KickoffUtc < end);
        }

        query = mode == "results"
            ? query.OrderByDescending(m => m.KickoffUtc)
            : query.OrderBy(m => m.KickoffUtc);

        var matches = await query
            .Take(100)
            .Select(HomeController.MatchProjection())
            .ToListAsync(ct);

        foreach (var m in matches)
            m.IsFavorite = favoriteMatchIds.Contains(m.MatchId);

        var leagues = await _db.Leagues
            .Where(l => l.IsTracked)
            .OrderBy(l => l.DisplayOrder)
            .Select(l => new LeagueFilterItem
            {
                Id = l.Id,
                Name = l.Name,
                LogoUrl = l.LogoUrl,
                MatchCount = l.Matches.Count()
            })
            .AsNoTracking()
            .ToListAsync(ct);

        return View(new MatchListViewModel
        {
            Matches = matches,
            Leagues = leagues,
            SelectedLeagueId = leagueId,
            SelectedDate = date ?? DateTime.UtcNow.Date,
            Mode = mode
        });
    }

    public async Task<IActionResult> Detail(int id, CancellationToken ct)
    {
        var match = await BaseQuery().FirstOrDefaultAsync(m => m.Id == id, ct);
        if (match is null) return NotFound();

        var item = await BaseQuery()
            .Where(m => m.Id == id)
            .Select(HomeController.MatchProjection())
            .FirstAsync(ct);

        if (User.Identity?.IsAuthenticated == true)
        {
            var uid = _userManager.GetUserId(User)!;
            var favMatches = await _favorites.GetMatchIdsAsync(uid, ct);
            var favTeams = await _favorites.GetTeamIdsAsync(uid, ct);

            item.IsFavorite = favMatches.Contains(id);
            ViewBag.HomeTeamFavorite = favTeams.Contains(match.HomeTeamId);
            ViewBag.AwayTeamFavorite = favTeams.Contains(match.AwayTeamId);
            ViewBag.HomeTeamId = match.HomeTeamId;
            ViewBag.AwayTeamId = match.AwayTeamId;
        }

        var model = new MatchDetailViewModel { Match = item };

        if (!User.Identity?.IsAuthenticated ?? true)
        {
            model.PredictionLocked = true;
            model.LockReason = "Tahminleri görmek için giriş yapmalısınız.";
            return View(model);
        }

        var userId = _userManager.GetUserId(User)!;
        var quota = await _subscription.ConsumeQuotaAsync(userId, ct);

        model.QuotaUsed = quota.Used;
        model.QuotaLimit = quota.Limit;

        if (!quota.Allowed)
        {
            model.PredictionLocked = true;
            model.LockReason = quota.Reason;
            return View(model);
        }

        model.Prediction = match.Prediction ?? await _engine.PredictAsync(id, false, ct);
        model.HomeStrength = await _strength.GetStrengthAsync(match.HomeTeamId, match.LeagueId, match.Season, ct);
        model.AwayStrength = await _strength.GetStrengthAsync(match.AwayTeamId, match.LeagueId, match.Season, ct);
        model.HomeForm = await _form.GetFormAsync(match.HomeTeamId, 5, ct);
        model.AwayForm = await _form.GetFormAsync(match.AwayTeamId, 5, ct);
        model.H2H = await _h2h.GetH2HAsync(match.HomeTeamId, match.AwayTeamId, 10, ct);

        ViewBag.Context = await _db.MatchContexts.AsNoTracking()
            .FirstOrDefaultAsync(c => c.MatchId == id, ct);

        if (model.Prediction is not null)
        {
            var xgH = model.Prediction.ExpectedHomeGoals;
            var xgA = model.Prediction.ExpectedAwayGoals;

            model.TopScores = PoissonMarkets.TopScores(xgH, xgA, 6);
            model.GoalMarkets = new List<(string, double)>
            {
                ("1.5 Üst", PoissonMarkets.OverProbability(xgH, xgA, 1.5)),
                ("2.5 Üst", PoissonMarkets.OverProbability(xgH, xgA, 2.5)),
                ("3.5 Üst", PoissonMarkets.OverProbability(xgH, xgA, 3.5)),
                ("KG Var", PoissonMarkets.BttsProbability(xgH, xgA))
            };
        }

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> LiveJson(CancellationToken ct)
    {
        var live = await BaseQuery()
            .Where(m => m.Status == MatchStatus.Live || m.Status == MatchStatus.HalfTime)
            .OrderBy(m => m.KickoffUtc)
            .Select(HomeController.MatchProjection())
            .ToListAsync(ct);

        return Json(live.Select(m => new
        {
            m.MatchId,
            m.HomeTeamName,
            m.AwayTeamName,
            m.HomeGoals,
            m.AwayGoals,
            m.ElapsedMinutes,
            Status = m.StatusText
        }));
    }

    private IQueryable<Match> BaseQuery() => _db.Matches
        .Include(m => m.League)
        .Include(m => m.HomeTeam)
        .Include(m => m.AwayTeam)
        .Include(m => m.Prediction)
        .AsNoTracking();
}