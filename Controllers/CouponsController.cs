using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ZokaAnalytics.Data;
using ZokaAnalytics.Models;
using ZokaAnalytics.Services.Coupons;
using ZokaAnalytics.ViewModels;

namespace ZokaAnalytics.Controllers;

[Authorize]
public class CouponsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly ICouponService _coupons;
    private readonly IWeeklyCouponBuilder _weekly;
    private readonly UserManager<ApplicationUser> _userManager;

    public CouponsController(
        ApplicationDbContext db,
        ICouponService coupons,
        IWeeklyCouponBuilder weekly,
        UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _coupons = coupons;
        _weekly = weekly;
        _userManager = userManager;
    }

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var userId = _userManager.GetUserId(User)!;

        var available = await _db.Matches
            .Include(m => m.League)
            .Include(m => m.HomeTeam)
            .Include(m => m.AwayTeam)
            .Include(m => m.Prediction)
            .Where(m => m.Status == MatchStatus.Scheduled
                        && m.KickoffUtc > DateTime.UtcNow
                        && m.Prediction != null)
            .OrderBy(m => m.KickoffUtc)
            .Take(60)
            .Select(HomeController.MatchProjection())
            .AsNoTracking()
            .ToListAsync(ct);

        var raw = await _coupons.BuildSuggestionsAsync(5, ct);
        var matchLookup = await _db.Matches
            .Include(m => m.HomeTeam).Include(m => m.AwayTeam).Include(m => m.League)
            .Where(m => raw.Select(r => r.Item1).Contains(m.Id))
            .AsNoTracking()
            .ToDictionaryAsync(m => m.Id, ct);

        var suggestions = raw
            .Where(r => matchLookup.ContainsKey(r.Item1))
            .Select(r =>
            {
                var m = matchLookup[r.Item1];
                return new SuggestionItem
                {
                    MatchId = r.Item1,
                    Selection = r.Item2,
                    Label = r.Item3,
                    Odds = r.Item4,
                    Probability = r.Item5,
                    HomeTeamName = m.HomeTeam.Name,
                    AwayTeamName = m.AwayTeam.Name,
                    KickoffUtc = m.KickoffUtc,
                    LeagueName = m.League.Name
                };
            })
            .ToList();

        return View(new CouponBuilderViewModel
        {
            AvailableMatches = available,
            Suggestions = suggestions,
            MyCoupons = await _coupons.GetUserCouponsAsync(userId, ct)
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        string title, decimal stake, int[] matchIds, string[] selections, CancellationToken ct)
    {
        var userId = _userManager.GetUserId(User)!;

        if (matchIds is null || matchIds.Length == 0)
        {
            TempData["Error"] = "En az bir mac secmelisiniz.";
            return RedirectToAction(nameof(Index));
        }

        var picks = new List<SelectionInput>();

        for (var i = 0; i < matchIds.Length && i < selections.Length; i++)
        {
            if (Enum.TryParse<BetType>(selections[i], out var bet))
                picks.Add(new SelectionInput(matchIds[i], bet));
        }

        var result = await _coupons.CreateAsync(userId, title, stake, picks, ct);

        if (!result.Success)
            TempData["Error"] = result.Error;
        else
            TempData["Success"] = $"Kupon olusturuldu (#{result.CouponId}).";

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Weekly(CancellationToken ct)
        => View(await _weekly.BuildAsync(7, ct));

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateFromWeekly(
        string couponName, decimal stake, int[] matchIds, string[] selections, CancellationToken ct)
    {
        var userId = _userManager.GetUserId(User)!;
        var picks = new List<SelectionInput>();

        for (var i = 0; i < matchIds.Length && i < selections.Length; i++)
        {
            if (Enum.TryParse<BetType>(selections[i], out var bet))
                picks.Add(new SelectionInput(matchIds[i], bet));
        }

        var result = await _coupons.CreateAsync(userId, $"{couponName} Kupon", stake, picks, ct);

        if (!result.Success)
        {
            TempData["Error"] = result.Error;
            return RedirectToAction(nameof(Weekly));
        }

        TempData["Success"] = "Kupon eklendi.";
        return RedirectToAction(nameof(Detail), new { id = result.CouponId });
    }

    public async Task<IActionResult> Detail(int id, CancellationToken ct)
    {
        var userId = _userManager.GetUserId(User)!;
        var coupon = await _coupons.GetAsync(id, userId, ct);

        return coupon is null ? NotFound() : View(coupon);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var userId = _userManager.GetUserId(User)!;
        await _coupons.DeleteAsync(id, userId, ct);

        TempData["Success"] = "Kupon silindi.";
        return RedirectToAction(nameof(Index));
    }
}
