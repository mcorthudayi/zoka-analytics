namespace ZokaAnalytics.Services.Sync;

public static class SeasonResolver
{
    private const int SeasonStartMonth = 7;

    public static int FromKickoff(DateTime kickoffUtc)
        => kickoffUtc.Month >= SeasonStartMonth ? kickoffUtc.Year : kickoffUtc.Year - 1;

    public static string Label(int season) => $"{season}/{season + 1}";
}
