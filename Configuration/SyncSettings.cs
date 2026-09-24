namespace ZokaAnalytics.Configuration;

public class SyncSettings
{
    public const string SectionName = "SyncSettings";

    public bool Enabled { get; set; } = true;
    public int RunAtUtcHour { get; set; } = 3;
    public int LiveRefreshSeconds { get; set; } = 60;

    public int HistoryDays { get; set; } = 45;

    public int UpcomingDays { get; set; } = 10;

    public int BackfillDays { get; set; } = 35;
}