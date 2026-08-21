using System.Text.Json.Serialization;

namespace FullTime.Api.BetBuilder.Dtos;

public class MatchesResponse
{
    [JsonPropertyName("data")]
    public List<MatchDto> Data { get; set; } = [];

    [JsonPropertyName("pagination")]
    public PaginationDto? Pagination { get; set; }
}

public class MatchDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("date")]
    public DateTime Date { get; set; }

    [JsonPropertyName("state")]
    public required MatchStateDto State { get; set; }

    [JsonPropertyName("homeTeam")]
    public required TeamDto HomeTeam { get; set; }

    [JsonPropertyName("awayTeam")]
    public required TeamDto AwayTeam { get; set; }
}

public class MatchStateDto
{
    [JsonPropertyName("description")]
    public required string Description { get; set; }
}

public class TeamDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public required string Name { get; set; }
}

public class PaginationDto
{
    [JsonPropertyName("totalCount")]
    public int TotalCount { get; set; }

    [JsonPropertyName("offset")]
    public int Offset { get; set; }

    [JsonPropertyName("limit")]
    public int Limit { get; set; }
}

public class OddsResponse
{
    [JsonPropertyName("data")]
    public List<MatchOddsDto> Data { get; set; } = [];

    [JsonPropertyName("pagination")]
    public PaginationDto? Pagination { get; set; }
}

public class MatchOddsDto
{
    [JsonPropertyName("matchId")]
    public long MatchId { get; set; }

    [JsonPropertyName("odds")]
    public List<OddsEntryDto> Odds { get; set; } = [];
}

public class OddsEntryDto
{
    [JsonPropertyName("type")]
    public required string Type { get; set; }

    [JsonPropertyName("market")]
    public required string Market { get; set; }

    [JsonPropertyName("values")]
    public List<OddsValueDto> Values { get; set; } = [];

    [JsonPropertyName("bookmakerName")]
    public required string BookmakerName { get; set; }
}

public class OddsValueDto
{
    [JsonPropertyName("odd")]
    public decimal Odd { get; set; }

    [JsonPropertyName("value")]
    public required string Value { get; set; }
}

// Goal timeline entry from /football/events/{id} — only used to resolve First Team To Score once
// a match finishes (see BetBuilderSyncService.ResolveFirstGoalScorersAsync). Response is a bare
// JSON array, not wrapped in a "data" envelope like the other endpoints.
public class MatchEventDto
{
    [JsonPropertyName("team")]
    public required TeamDto Team { get; set; }

    [JsonPropertyName("time")]
    public required string Time { get; set; }

    [JsonPropertyName("type")]
    public required string Type { get; set; }
}
