namespace ZokaAnalytics.ViewModels;

public class HomeViewModel
{
    public List<MatchListItemViewModel> TodayMatches { get; set; } = new();
    public List<MatchListItemViewModel> TopPredictions { get; set; } = new();
    public List<MatchListItemViewModel> LiveMatches { get; set; } = new();

    public int TotalMatches { get; set; }
    public int TotalPredictions { get; set; }
    public double PredictionAccuracy { get; set; }
    public int SettledPredictions { get; set; }
    public bool ModelTrained { get; set; }
}

public class ErrorViewModel
{
    public string? RequestId { get; set; }
    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
    public string? Message { get; set; }
}