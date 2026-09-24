using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ZokaAnalytics.Configuration;
using ZokaAnalytics.Data;
using ZokaAnalytics.Models;
using ZokaAnalytics.Services.Analysis;
using ZokaAnalytics.Services.FotMob;
using ZokaAnalytics.Services.Coupons;
using ZokaAnalytics.Services.Demo;
using ZokaAnalytics.Services.Favorites;
using ZokaAnalytics.Services.Prediction;
using ZokaAnalytics.Services.Sync;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("DefaultConnection bulunamadi.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.Password.RequireDigit = true;
        options.Password.RequiredLength = 6;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = true;
        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedAccount = false;
        options.Lockout.MaxFailedAccessAttempts = 8;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromDays(14);
    options.SlidingExpiration = true;
});

builder.Services.Configure<ApiFootballOptions>(
    builder.Configuration.GetSection(ApiFootballOptions.SectionName));
builder.Services.Configure<SyncSettings>(
    builder.Configuration.GetSection(SyncSettings.SectionName));

builder.Services.AddHttpClient<IFootballDataClient, FotMobClient>((sp, client) =>
{
    var options = builder.Configuration
        .GetSection(ApiFootballOptions.SectionName)
        .Get<ApiFootballOptions>() ?? new ApiFootballOptions();

    client.BaseAddress = new Uri(options.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Add("x-rapidapi-key", options.RapidApiKey);
    client.DefaultRequestHeaders.Add("x-rapidapi-host", options.RapidApiHost);
});

builder.Services.AddHttpClient("football-raw", client =>
{
    var options = builder.Configuration
        .GetSection(ApiFootballOptions.SectionName)
        .Get<ApiFootballOptions>() ?? new ApiFootballOptions();

    client.BaseAddress = new Uri(options.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Add("x-rapidapi-key", options.RapidApiKey);
    client.DefaultRequestHeaders.Add("x-rapidapi-host", options.RapidApiHost);
});

builder.Services.AddScoped<ISyncRunner, SyncRunner>();
builder.Services.AddScoped<ITeamStrengthService, TeamStrengthService>();
builder.Services.AddScoped<IStandingsService, StandingsService>();
builder.Services.AddScoped<IFormService, FormService>();
builder.Services.AddScoped<IH2HService, H2HService>();
builder.Services.AddScoped<IMatchPredictionEngine, MatchPredictionEngine>();
builder.Services.AddScoped<IPredictionTrainer, PredictionTrainer>();
builder.Services.AddScoped<ISubscriptionService, SubscriptionService>();
builder.Services.AddScoped<IDemoDataService, DemoDataService>();
builder.Services.AddScoped<ICouponService, CouponService>();
builder.Services.AddScoped<IWeeklyCouponBuilder, WeeklyCouponBuilder>();
builder.Services.AddScoped<IFavoriteService, FavoriteService>();
builder.Services.AddSingleton<IMlModelStore, MlModelStore>();

builder.Services.AddHostedService<DailySyncBackgroundService>();

builder.Services.AddControllersWithViews();
builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();

var app = builder.Build();

await DbSeeder.SeedAsync(app.Services, app.Configuration);
app.Services.GetRequiredService<IMlModelStore>().TryLoadFromDisk();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapGet("/health", async (ApplicationDbContext db, IMlModelStore store) =>
{
    var canConnect = await db.Database.CanConnectAsync();
    return Results.Ok(new
    {
        status = canConnect ? "ok" : "db-unreachable",
        modelLoaded = store.IsLoaded,
        leagues = canConnect ? await db.Leagues.CountAsync() : 0,
        teams = canConnect ? await db.Teams.CountAsync() : 0,
        matches = canConnect ? await db.Matches.CountAsync() : 0,
        predictions = canConnect ? await db.Predictions.CountAsync() : 0,
        utc = DateTime.UtcNow
    });
});

if (app.Environment.IsDevelopment())
{
    app.MapGet("/dev/wipe", async (ApplicationDbContext db, IConfiguration config, CancellationToken ct) =>
    {
        await db.Predictions.ExecuteDeleteAsync(ct);
        await db.CouponItems.ExecuteDeleteAsync(ct);
        await db.Coupons.ExecuteDeleteAsync(ct);
        await db.H2HRecords.ExecuteDeleteAsync(ct);
        await db.TeamStatistics.ExecuteDeleteAsync(ct);
        await db.Matches.ExecuteDeleteAsync(ct);
        await db.Teams.ExecuteDeleteAsync(ct);
        await db.SyncLogs.ExecuteDeleteAsync(ct);
        await db.Leagues.ExecuteDeleteAsync(ct);

        var season = config.GetValue("ApiFootball:CurrentSeason", 2026);
        db.Leagues.AddRange(
            new League { Id = 71, Name = "Super Lig", Country = "TUR", CurrentSeason = season, IsTracked = true, DisplayOrder = 1 },
            new League { Id = 47, Name = "Premier League", Country = "ENG", CurrentSeason = season, IsTracked = true, DisplayOrder = 2 },
            new League { Id = 87, Name = "LaLiga", Country = "ESP", CurrentSeason = season, IsTracked = true, DisplayOrder = 3 },
            new League { Id = 55, Name = "Serie A", Country = "ITA", CurrentSeason = season, IsTracked = true, DisplayOrder = 4 },
            new League { Id = 54, Name = "Bundesliga", Country = "GER", CurrentSeason = season, IsTracked = true, DisplayOrder = 5 },
            new League { Id = 53, Name = "Ligue 1", Country = "FRA", CurrentSeason = season, IsTracked = true, DisplayOrder = 6 },
            new League { Id = 42, Name = "Champions League", Country = "INT", CurrentSeason = season, IsTracked = true, DisplayOrder = 7 }
        );
        await db.SaveChangesAsync(ct);

        return Results.Ok(new { message = "Veritabani temizlendi, FotMob ligleri kuruldu. Simdi /dev/sync calistir." });
    });

    app.MapGet("/dev/demo", async (IDemoDataService demo, bool reset, CancellationToken ct) =>
        Results.Ok(await demo.GenerateAsync(reset, ct)));

    app.MapGet("/dev/probe", async (IHttpClientFactory factory, string path, CancellationToken ct) =>
    {
        var client = factory.CreateClient("football-raw");
        var response = await client.GetAsync(path, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        return Results.Text($"HTTP {(int)response.StatusCode}\n\n{body[..Math.Min(body.Length, 6000)]}",
            "text/plain; charset=utf-8");
    });

    app.MapGet("/dev/reseason", async (ApplicationDbContext db, ISyncRunner sync, CancellationToken ct) =>
    {
        var matches = await db.Matches.ToListAsync(ct);
        var changed = 0;

        foreach (var match in matches)
        {
            var correct = ZokaAnalytics.Services.Sync.SeasonResolver.FromKickoff(match.KickoffUtc);
            if (match.Season == correct) continue;

            match.Season = correct;
            changed++;
        }

        await db.SaveChangesAsync(ct);

        await db.TeamStatistics.ExecuteDeleteAsync(ct);
        var summary = await sync.RunFullSyncAsync(ct);

        var breakdown = await db.Matches
            .GroupBy(m => m.Season)
            .Select(g => new { Season = g.Key, Matches = g.Count() })
            .OrderByDescending(x => x.Season)
            .ToListAsync(ct);

        return Results.Ok(new { changed, breakdown, syncSteps = summary.Steps });
    });

    app.MapGet("/dev/sync", async (ISyncRunner runner, CancellationToken ct) =>
        Results.Ok(await runner.RunFullSyncAsync(ct)));

    app.MapGet("/dev/live", async (ISyncRunner runner, CancellationToken ct) =>
        Results.Ok(new { updated = await runner.RefreshLiveMatchesAsync(ct) }));

    app.MapGet("/dev/train", async (IPredictionTrainer trainer, CancellationToken ct) =>
        Results.Ok(await trainer.TrainAsync(ct)));

    app.MapGet("/dev/predict", async (IMatchPredictionEngine engine, CancellationToken ct) =>
        Results.Ok(new { created = await engine.PredictUpcomingAsync(21, ct) }));

    app.MapGet("/dev/settle", async (IMatchPredictionEngine engine, CancellationToken ct) =>
        Results.Ok(new { settled = await engine.SettlePredictionsAsync(ct) }));
}

app.Run();
