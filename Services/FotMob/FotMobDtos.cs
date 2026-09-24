using System.Text.Json.Serialization;

namespace ZokaAnalytics.Services.FotMob;

public class FmTeamRef
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("longName")] public string? LongName { get; set; }
    [JsonPropertyName("score")] public int? Score { get; set; }
}

public class FmLiveTime
{
    [JsonPropertyName("short")] public string? Short { get; set; }
    [JsonPropertyName("maxTime")] public int? MaxTime { get; set; }
}

public class FmStatus
{
    [JsonPropertyName("utcTime")] public DateTimeOffset? UtcTime { get; set; }
    [JsonPropertyName("finished")] public bool Finished { get; set; }
    [JsonPropertyName("started")] public bool Started { get; set; }
    [JsonPropertyName("cancelled")] public bool Cancelled { get; set; }
    [JsonPropertyName("ongoing")] public bool Ongoing { get; set; }
    [JsonPropertyName("scoreStr")] public string? ScoreStr { get; set; }
    [JsonPropertyName("liveTime")] public FmLiveTime? LiveTime { get; set; }
}

public class FmMatch
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("leagueId")] public long? LeagueId { get; set; }
    [JsonPropertyName("home")] public FmTeamRef Home { get; set; } = new();
    [JsonPropertyName("away")] public FmTeamRef Away { get; set; } = new();
    [JsonPropertyName("status")] public FmStatus Status { get; set; } = new();
    [JsonPropertyName("tournamentStage")] public string? TournamentStage { get; set; }
    [JsonPropertyName("round")] public string? Round { get; set; }
    [JsonPropertyName("notStarted")] public bool? NotStarted { get; set; }

    public string? RoundLabel => !string.IsNullOrWhiteSpace(Round) ? Round
        : !string.IsNullOrWhiteSpace(TournamentStage) ? $"Hafta {TournamentStage}"
        : null;
}

public class FmLeagueDetail
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("shortName")] public string? ShortName { get; set; }
    [JsonPropertyName("country")] public string? Country { get; set; }
    [JsonPropertyName("selectedSeason")] public string? SelectedSeason { get; set; }

    public int? SeasonYear
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SelectedSeason)) return null;
            var digits = new string(SelectedSeason.TakeWhile(char.IsDigit).ToArray());
            return int.TryParse(digits, out var year) && year > 1900 ? year : null;
        }
    }
}

public static class FotMobImages
{
    public static string TeamLogo(int teamId)
        => $"https://images.fotmob.com/image_resources/logo/teamlogo/{teamId}.png";

    public static string LeagueLogo(int leagueId)
        => $"https://images.fotmob.com/image_resources/logo/leaguelogo/dark/{leagueId}.png";
}
