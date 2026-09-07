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

    [JsonPropertyName("league")]
    public required LeagueDto League { get; set; }

    // Confirmed real 2026-09-06 against the direct Highlightly account (soccer.highlightly.net) -
    // a bare top-level string, e.g. "Regular Season - 3" for league matches, "1st Round Qualifying"
    // for FA Cup. Used by HighlightlyMatchSyncService.IsEligibleFaCupRound to filter FA Cup's early
    // non-league rounds out of sync entirely.
    [JsonPropertyName("round")]
    public string? Round { get; set; }
}

public class MatchStateDto
{
    [JsonPropertyName("description")]
    public required string Description { get; set; }

    [JsonPropertyName("score")]
    public ScoreDto? Score { get; set; }

    // Current match minute — null before kickoff, confirmed a plain int (e.g. 90 for a finished
    // match) rather than an object with extra-time detail.
    [JsonPropertyName("clock")]
    public int? Clock { get; set; }
}

// "current" is a "H - A" string (e.g. "1 - 2"), null before kickoff.
public class ScoreDto
{
    [JsonPropertyName("current")]
    public string? Current { get; set; }
}

public class TeamDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("logo")]
    public string? Logo { get; set; }
}

public class LeagueDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("logo")]
    public string? Logo { get; set; }
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

// Full match-timeline entry from /events/{id} - used both to resolve First Team To Score once a
// match finishes and (as of 2026-09-07) to store the full goal/card/substitution timeline for
// display (see BetBuilderSyncService.ResolveMatchEventsAsync, Models/MatchEvent.cs). Response is a
// bare JSON array, not wrapped in a "data" envelope like the other endpoints. Player/assist/
// substitution fields confirmed real 2026-09-07 - previously fetched and silently discarded.
public class MatchEventDto
{
    [JsonPropertyName("team")]
    public required TeamDto Team { get; set; }

    [JsonPropertyName("time")]
    public required string Time { get; set; }

    [JsonPropertyName("type")]
    public required string Type { get; set; }

    // Player coming ON for a Substitution, the scorer for a Goal, the booked player for a card.
    [JsonPropertyName("player")]
    public string? Player { get; set; }

    [JsonPropertyName("playerId")]
    public long? PlayerId { get; set; }

    // Assist provider's name, set only for Goal events with one.
    [JsonPropertyName("assist")]
    public string? Assist { get; set; }

    [JsonPropertyName("assistingPlayerId")]
    public long? AssistingPlayerId { get; set; }

    // The player coming OFF, set only for Substitution events.
    [JsonPropertyName("substituted")]
    public string? Substituted { get; set; }
}

// Team-level (not per-player - confirmed real 2026-09-07, Highlightly has no per-player stats
// endpoint) match statistics from /statistics/{matchId}. Only used for the "Corners" entry
// (MarketType.TotalCorners settlement, see BetBuilderSyncService.ResolveMatchEventsAsync) - the
// dozens of other stats it returns (passes, xG, aerial duels, etc.) aren't modelled since nothing
// settles off them.
public class TeamStatisticsDto
{
    [JsonPropertyName("team")]
    public required TeamDto Team { get; set; }

    [JsonPropertyName("statistics")]
    public List<StatisticEntryDto> Statistics { get; set; } = [];
}

public class StatisticEntryDto
{
    // A plain number, but not always a whole one (e.g. "Possession": 0.45, "Expected Goals": 1.01) -
    // double rather than int so parsing never fails regardless of which stat this entry is.
    [JsonPropertyName("value")]
    public double? Value { get; set; }

    [JsonPropertyName("displayName")]
    public required string DisplayName { get; set; }
}
