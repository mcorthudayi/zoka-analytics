using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ZokaAnalytics.Models;

namespace ZokaAnalytics.Data;

public static class DbSeeder
{
    public const string AdminRole = "Admin";
    public const string UserRole = "User";

    public static async Task SeedAsync(IServiceProvider services, IConfiguration configuration)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;

        var db = sp.GetRequiredService<ApplicationDbContext>();
        var roleManager = sp.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("DbSeeder");

        await db.Database.MigrateAsync();

        foreach (var role in new[] { AdminRole, UserRole })
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }

        var adminEmail = configuration["AdminSeed:Email"] ?? "admin@zoka.local";
        var adminPassword = configuration["AdminSeed:Password"] ?? throw new InvalidOperationException("AdminSeed:Password is not configured. Set it with: dotnet user-secrets set \"AdminSeed:Password\" \"<password>\"");
        var adminName = configuration["AdminSeed:FullName"] ?? "ZOKA Admin";

        var admin = await userManager.FindByEmailAsync(adminEmail);
        if (admin is null)
        {
            admin = new ApplicationUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                EmailConfirmed = true,
                FullName = adminName,
                Tier = SubscriptionTier.Elite,
                SubscriptionExpiresAtUtc = DateTime.UtcNow.AddYears(10)
            };

            var result = await userManager.CreateAsync(admin, adminPassword);
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(admin, AdminRole);
                logger.LogInformation("Admin kullanıcı oluşturuldu: {Email}", adminEmail);
            }
            else
            {
                logger.LogError("Admin oluşturulamadı: {Errors}",
                    string.Join(", ", result.Errors.Select(e => e.Description)));
            }
        }
        else if (!await userManager.IsInRoleAsync(admin, AdminRole))
        {
            await userManager.AddToRoleAsync(admin, AdminRole);
        }

        if (!await db.Leagues.AnyAsync())
        {
            var season = configuration.GetValue("ApiFootball:CurrentSeason", 2026);

            db.Leagues.AddRange(
                NewLeague(71, "Super Lig", "TUR", season, 1),
                NewLeague(47, "Premier League", "ENG", season, 2),
                NewLeague(87, "LaLiga", "ESP", season, 3),
                NewLeague(55, "Serie A", "ITA", season, 4),
                NewLeague(54, "Bundesliga", "GER", season, 5),
                NewLeague(53, "Ligue 1", "FRA", season, 6),
                NewLeague(42, "Champions League", "INT", season, 7)
            );

            await db.SaveChangesAsync();
            logger.LogInformation("Varsayılan ligler eklendi.");
        }
    }

    private static League NewLeague(int id, string name, string country, int season, int order) => new()
    {
        Id = id,
        Name = name,
        Country = country,
        CurrentSeason = season,
        IsTracked = true,
        DisplayOrder = order
    };
}