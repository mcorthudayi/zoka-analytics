namespace ZokaAnalytics.Services.Analysis;

public record StandingRow(
    int Rank,
    int TeamId,
    string TeamName,
    string? LogoUrl,
    int Played,
    int Wins,
    int Draws,
    int Losses,
    int GoalsFor,
    int GoalsAgainst,
    int Points,
    string Form,
    double Strength)
{
    public int GoalDifference => GoalsFor - GoalsAgainst;
    public double PointsPerGame => Played > 0 ? Math.Round((double)Points / Played, 2) : 0;
}

public record StandingsTable(
    int LeagueId,
    string LeagueName,
    string? LeagueLogoUrl,
    int Season,
    List<StandingRow> Rows,
    DateTime? LastUpdatedUtc);

public interface IStandingsService
{
    Task<StandingsTable?> GetAsync(int leagueId, int? season = null, string scope = "all", CancellationToken ct = default);
    Task<List<int>> GetAvailableSeasonsAsync(int leagueId, CancellationToken ct = default);
}
