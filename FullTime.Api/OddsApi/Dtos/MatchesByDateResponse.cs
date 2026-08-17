using System.Text.Json.Serialization;

namespace FullTime.Api.OddsApi.Dtos;

public class MatchesByDateResponse
{
    [JsonPropertyName("response")]
    public MatchesByDateResult? Response { get; set; }
}

public class MatchesByDateResult
{
    [JsonPropertyName("matches")]
    public List<MatchDto> Matches { get; set; } = [];
}

public class MatchDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("leagueId")]
    public long LeagueId { get; set; }

    [JsonPropertyName("home")]
    public required TeamDto Home { get; set; }

    [JsonPropertyName("away")]
    public required TeamDto Away { get; set; }

    [JsonPropertyName("status")]
    public required MatchStatusDto Status { get; set; }
}

public class TeamDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("score")]
    public int Score { get; set; }
}

public class MatchStatusDto
{
    [JsonPropertyName("utcTime")]
    public DateTime UtcTime { get; set; }

    [JsonPropertyName("started")]
    public bool Started { get; set; }

    [JsonPropertyName("finished")]
    public bool Finished { get; set; }

    [JsonPropertyName("cancelled")]
    public bool Cancelled { get; set; }

    [JsonPropertyName("scoreStr")]
    public string? ScoreStr { get; set; }
}
