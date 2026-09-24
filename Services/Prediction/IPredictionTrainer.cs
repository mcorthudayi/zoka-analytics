using Microsoft.ML;

namespace ZokaAnalytics.Services.Prediction;

public record TrainingResult(
    bool Success,
    int SamplesUsed,
    double MacroAccuracy,
    double LogLoss,
    string Message);

public interface IPredictionTrainer
{
    Task<TrainingResult> TrainAsync(CancellationToken ct = default);
}

public interface IMlModelStore
{
    bool IsLoaded { get; }
    PredictionEngine<MatchFeatures, MatchPredictionOutput> GetPredictionEngine();
    string[] GetLabels();
    void Load(ITransformer model, DataViewSchema schema, string[] labels);
    void TryLoadFromDisk();
    void SaveToDisk(ITransformer model, DataViewSchema schema, string[] labels);
}
