using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ZokaAnalytics.Configuration;

namespace ZokaAnalytics.Services.FotMob;

public class FotMobClient : IFootballDataClient
{
    private readonly HttpClient _http;
    private readonly ApiFootballOptions _options;
    private readonly ILogger<FotMobClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
    };

    private int _callsUsed;

    public FotMobClient(HttpClient http, IOptions<ApiFootballOptions> options, ILogger<FotMobClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public int CallsUsed => _callsUsed;
    public bool BudgetExceeded => _callsUsed >= _options.DailyCallBudget;

    public async Task<FmLeagueDetail?> GetLeagueDetailAsync(int leagueId, CancellationToken ct = default)
    {
        var response = await GetResponseAsync($"football-get-league-detail?leagueid={leagueId}", ct);
        if (response is null) return null;

        if (!response.Value.TryGetProperty("leagues", out var leagues)) return null;

        var element = leagues.ValueKind == JsonValueKind.Array
            ? (leagues.GetArrayLength() > 0 ? leagues[0] : default)
            : leagues;

        if (element.ValueKind != JsonValueKind.Object) return null;

        return element.Deserialize<FmLeagueDetail>(JsonOptions);
    }

    public Task<List<FmMatch>> GetLeagueMatchesAsync(int leagueId, CancellationToken ct = default)
        => GetMatchListAsync($"football-get-all-matches-by-league?leagueid={leagueId}", "matches", ct);

    public Task<List<FmMatch>> GetMatchesByDateAsync(DateOnly date, CancellationToken ct = default)
        => GetMatchListAsync(
            $"football-get-matches-by-date?date={date.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}",
            "matches", ct);

    public Task<List<FmMatch>> GetLiveMatchesAsync(CancellationToken ct = default)
        => GetMatchListAsync("football-current-live", "live", ct);

    private async Task<List<FmMatch>> GetMatchListAsync(string url, string key, CancellationToken ct)
    {
        var response = await GetResponseAsync(url, ct);
        if (response is null) return new List<FmMatch>();

        if (!response.Value.TryGetProperty(key, out var array) || array.ValueKind != JsonValueKind.Array)
            return new List<FmMatch>();

        try
        {
            return array.Deserialize<List<FmMatch>>(JsonOptions) ?? new List<FmMatch>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Mac listesi cozumlenemedi: {Url}", url);
            return new List<FmMatch>();
        }
    }

    private async Task<JsonElement?> GetResponseAsync(string relativeUrl, CancellationToken ct)
    {
        if (!_options.IsConfigured)
        {
            _logger.LogWarning("API anahtari ayarli degil, istek atlaniyor: {Url}", relativeUrl);
            return null;
        }

        if (BudgetExceeded)
        {
            _logger.LogWarning("Gunluk cagri butcesi doldu ({Used}): {Url}", _callsUsed, relativeUrl);
            return null;
        }

        try
        {
            _callsUsed++;
            using var httpResponse = await _http.GetAsync(relativeUrl, ct);
            var body = await httpResponse.Content.ReadAsStringAsync(ct);

            if (!httpResponse.IsSuccessStatusCode)
            {
                _logger.LogError("API {Status} dondu. Url: {Url}. Govde: {Body}",
                    (int)httpResponse.StatusCode, relativeUrl, Truncate(body, 400));
                return null;
            }

            using var document = JsonDocument.Parse(body);

            if (!document.RootElement.TryGetProperty("response", out var response))
            {
                _logger.LogWarning("Yanitta 'response' alani yok: {Url}", relativeUrl);
                return null;
            }

            return response.Clone();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Istek basarisiz: {Url}", relativeUrl);
            return null;
        }
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max] + "...";
}
