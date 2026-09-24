using Microsoft.Extensions.Options;
using ZokaAnalytics.Configuration;

namespace ZokaAnalytics.Services.Sync;

public class DailySyncBackgroundService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly SyncSettings _settings;
    private readonly ILogger<DailySyncBackgroundService> _logger;

    public DailySyncBackgroundService(
        IServiceProvider services,
        IOptions<SyncSettings> settings,
        ILogger<DailySyncBackgroundService> logger)
    {
        _services = services;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("Sync devre dışı (SyncSettings:Enabled = false).");
            return;
        }

        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeUntilNextRun();
            _logger.LogInformation("Sonraki tam sync {Delay} sonra ({Hour}:00 UTC).", delay, _settings.RunAtUtcHour);

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                using var scope = _services.CreateScope();
                var runner = scope.ServiceProvider.GetRequiredService<ISyncRunner>();
                var summary = await runner.RunFullSyncAsync(stoppingToken);

                _logger.LogInformation("Tam sync bitti. {Calls} API çağrısı, {Ok}/{Total} adım başarılı.",
                    summary.ApiCallsUsed,
                    summary.Steps.Count(s => s.Success),
                    summary.Steps.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Tam sync sırasında beklenmeyen hata.");
            }
        }
    }

    private TimeSpan TimeUntilNextRun()
    {
        var now = DateTime.UtcNow;
        var next = new DateTime(now.Year, now.Month, now.Day, _settings.RunAtUtcHour, 0, 0, DateTimeKind.Utc);
        if (next <= now) next = next.AddDays(1);
        return next - now;
    }
}