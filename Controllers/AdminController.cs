using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ZokaAnalytics.Data;
using ZokaAnalytics.Models;
using ZokaAnalytics.Services.Coupons;
using ZokaAnalytics.Services.Prediction;
using ZokaAnalytics.Services.Sync;
using ZokaAnalytics.ViewModels;

namespace ZokaAnalytics.Controllers;

[Authorize(Roles = DbSeeder.AdminRole)]
public class AdminController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly ISyncRunner _sync;
    private readonly IPredictionTrainer _trainer;
    private readonly IMatchPredictionEngine _engine;
    private readonly ICouponService _coupons;
    private readonly ISubscriptionService _subscription;
    private readonly IMlModelStore _modelStore;

    public AdminController(
        ApplicationDbContext db,
        ISyncRunner sync,
        IPredictionTrainer trainer,
        IMatchPredictionEngine engine,
        ICouponService coupons,
        ISubscriptionService subscription,
        IMlModelStore modelStore)
    {
        _db = db;
        _sync = sync;
        _trainer = trainer;
        _engine = engine;
        _coupons = coupons;
        _subscription = subscription;
        _modelStore = modelStore;
    }

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var settled = await _db.Predictions.CountAsync(p => p.IsSettled, ct);
        var correct = await _db.Predictions.CountAsync(p => p.IsSettled && p.WasCorrect == true, ct);

        var model = new AdminDashboardViewModel
        {
            UserCount = await _db.Users.CountAsync(ct),
            MatchCount = await _db.Matches.CountAsync(ct),
            TeamCount = await _db.Teams.CountAsync(ct),
            PredictionCount = await _db.Predictions.CountAsync(ct),
            SettledPredictions = settled,
            Accuracy = settled > 0 ? Math.Round((double)correct / settled * 100, 1) : 0,
            CouponCount = await _db.Coupons.CountAsync(ct),
            ModelLoaded = _modelStore.IsLoaded,

            RecentUsers = await _db.Users
                .OrderByDescending(u => u.CreatedAtUtc)
                .Take(10)
                .AsNoTracking()
                .ToListAsync(ct),

            RecentSyncs = await _db.SyncLogs
                .OrderByDescending(s => s.StartedAtUtc)
                .Take(12)
                .AsNoTracking()
                .ToListAsync(ct),

            LeagueStats = await _db.Leagues
                .Where(l => l.IsTracked)
                .OrderBy(l => l.DisplayOrder)
                .Select(l => new LeagueStat
                {
                    Name = l.Name,
                    Matches = l.Matches.Count(),
                    Predictions = l.Matches.Count(m => m.Prediction != null),
                    LastSyncedAtUtc = l.LastSyncedAtUtc
                })
                .AsNoTracking()
                .ToListAsync(ct)
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RunSync(CancellationToken ct)
    {
        var summary = await _sync.RunFullSyncAsync(ct);
        TempData["Success"] = $"Sync tamam: {summary.Steps.Count(s => s.Success)}/{summary.Steps.Count} adim, " +
                              $"{summary.ApiCallsUsed} API cagrisi.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Train(CancellationToken ct)
    {
        var result = await _trainer.TrainAsync(ct);
        TempData[result.Success ? "Success" : "Error"] = result.Message;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Predict(CancellationToken ct)
    {
        var created = await _engine.PredictUpcomingAsync(21, ct);
        TempData["Success"] = $"{created} mac icin tahmin uretildi.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Settle(CancellationToken ct)
    {
        var predictions = await _engine.SettlePredictionsAsync(ct);
        var coupons = await _coupons.SettleAllAsync(ct);
        TempData["Success"] = $"{predictions} tahmin, {coupons} kupon sonuclandirildi.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Context(CancellationToken ct)
    {
        var matches = await _db.Matches
            .Include(m => m.League).Include(m => m.HomeTeam).Include(m => m.AwayTeam)
            .Where(m => m.Status == MatchStatus.Scheduled && m.KickoffUtc > DateTime.UtcNow)
            .OrderBy(m => m.KickoffUtc)
            .Take(30)
            .AsNoTracking()
            .ToListAsync(ct);

        var ids = matches.Select(m => m.Id).ToList();
        ViewBag.Contexts = await _db.MatchContexts
            .Where(c => ids.Contains(c.MatchId))
            .AsNoTracking()
            .ToDictionaryAsync(c => c.MatchId, ct);

        return View(matches);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveContext(
        int matchId,
        int homeKeyPlayersMissing, int awayKeyPlayersMissing,
        bool homeCoachChanged, bool awayCoachChanged,
        bool homeNothingToPlayFor, bool awayNothingToPlayFor,
        bool isDerby, string? note,
        CancellationToken ct)
    {
        var context = await _db.MatchContexts.FirstOrDefaultAsync(c => c.MatchId == matchId, ct);

        if (context is null)
        {
            context = new MatchContext { MatchId = matchId };
            _db.MatchContexts.Add(context);
        }

        context.HomeKeyPlayersMissing = Math.Clamp(homeKeyPlayersMissing, 0, 5);
        context.AwayKeyPlayersMissing = Math.Clamp(awayKeyPlayersMissing, 0, 5);
        context.HomeCoachChanged = homeCoachChanged;
        context.AwayCoachChanged = awayCoachChanged;
        context.HomeNothingToPlayFor = homeNothingToPlayFor;
        context.AwayNothingToPlayFor = awayNothingToPlayFor;
        context.IsDerby = isDerby;
        context.Note = note;
        context.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        await _engine.PredictAsync(matchId, forceRefresh: true, ct);

        TempData["Success"] = "Bağlam kaydedildi, tahmin yenilendi.";
        return RedirectToAction(nameof(Context));
    }

    public async Task<IActionResult> Users(CancellationToken ct)
        => View(await _db.Users.OrderByDescending(u => u.CreatedAtUtc).AsNoTracking().ToListAsync(ct));

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetTier(string userId, SubscriptionTier tier, int months, CancellationToken ct)
    {
        var ok = await _subscription.UpgradeAsync(userId, tier, months <= 0 ? 1 : months, ct);
        TempData[ok ? "Success" : "Error"] = ok ? "Paket guncellendi." : "Kullanici bulunamadi.";
        return RedirectToAction(nameof(Users));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleBan(string userId, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return NotFound();

        user.IsBanned = !user.IsBanned;
        await _db.SaveChangesAsync(ct);

        TempData["Success"] = user.IsBanned ? "Kullanici askiya alindi." : "Askiya alma kaldirildi.";
        return RedirectToAction(nameof(Users));
    }
}
