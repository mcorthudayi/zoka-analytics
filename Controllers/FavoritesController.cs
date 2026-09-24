using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using ZokaAnalytics.Models;
using ZokaAnalytics.Services.Favorites;

namespace ZokaAnalytics.Controllers;

[Authorize]
public class FavoritesController : Controller
{
    private readonly IFavoriteService _favorites;
    private readonly UserManager<ApplicationUser> _userManager;

    public FavoritesController(IFavoriteService favorites, UserManager<ApplicationUser> userManager)
    {
        _favorites = favorites;
        _userManager = userManager;
    }

    [HttpPost]
    public async Task<IActionResult> ToggleMatch(int matchId, CancellationToken ct)
    {
        var userId = _userManager.GetUserId(User)!;
        var isFavorite = await _favorites.ToggleMatchAsync(userId, matchId, ct);
        return Json(new { isFavorite });
    }

    [HttpPost]
    public async Task<IActionResult> ToggleTeam(int teamId, CancellationToken ct)
    {
        var userId = _userManager.GetUserId(User)!;
        var isFavorite = await _favorites.ToggleTeamAsync(userId, teamId, ct);
        return Json(new { isFavorite });
    }
}
