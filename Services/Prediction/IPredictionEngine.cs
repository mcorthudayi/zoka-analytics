namespace ZokaAnalytics.Services.Prediction;

public interface IMatchPredictionEngine
{
    Task<Models.Prediction?> PredictAsync(int matchId, bool forceRefresh = false, CancellationToken ct = default);
    Task<int> PredictUpcomingAsync(int days = 7, CancellationToken ct = default);
    Task<int> SettlePredictionsAsync(CancellationToken ct = default);
}
