namespace ZokaAnalytics.Configuration;

public class ApiFootballOptions
{
    public const string SectionName = "ApiFootball";

    public string BaseUrl { get; set; } = "https://api-football-v1.p.rapidapi.com/v3/";
    public string RapidApiHost { get; set; } = "api-football-v1.p.rapidapi.com";
    public string RapidApiKey { get; set; } = string.Empty;
    public int DailyCallBudget { get; set; } = 100;
    public int CurrentSeason { get; set; } = 2026;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(RapidApiKey) && RapidApiKey != "BURAYA_RAPIDAPI_KEYINI_YAZ";
}