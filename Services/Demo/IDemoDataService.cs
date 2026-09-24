namespace ZokaAnalytics.Services.Demo;

public record DemoSeedResult(int Teams, int Matches, int Statistics, string Message);

public interface IDemoDataService
{
    Task<DemoSeedResult> GenerateAsync(bool reset = false, CancellationToken ct = default);
}
