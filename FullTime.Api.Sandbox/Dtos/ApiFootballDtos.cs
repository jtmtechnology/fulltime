using System.Text.Json.Serialization;

namespace FullTime.Api.Sandbox.Dtos;

// Shapes confirmed against real responses from v3.football.api-sports.io on 2026-09-06 — see
// HANDOVER.md for the manual curl session this was built from. Only the fields the sync/test
// endpoints actually use are mapped; API-Football's real payloads carry more (venue, referee,
// periods, score.halftime/extratime/penalty, etc.) that we don't need yet.
public class ApiFootballResponse<T>
{
    [JsonPropertyName("results")]
    public int Results { get; set; }

    // api-football returns "errors" as `[]` when empty but `{"...": "..."}` when there's actually
    // an error - a shape that doesn't map cleanly to one .NET type. JsonElement accepts either, and
    // we only care whether it's empty (ValueKind == Array with no items).
    [JsonPropertyName("errors")]
    public System.Text.Json.JsonElement Errors { get; set; }

    [JsonPropertyName("response")]
    public List<T> Response { get; set; } = [];
}

public class FixtureDto
{
    [JsonPropertyName("fixture")]
    public required FixtureInfo Fixture { get; set; }

    [JsonPropertyName("league")]
    public required LeagueInfo League { get; set; }

    [JsonPropertyName("teams")]
    public required TeamsInfo Teams { get; set; }

    [JsonPropertyName("goals")]
    public GoalsInfo? Goals { get; set; }
}

public class FixtureInfo
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    // DateTimeOffset, not DateTime - api-football sends an explicit "+00:00" offset (not "Z"), and
    // System.Text.Json's default DateTime converter parses that as Kind=Local rather than Utc,
    // which Npgsql then rejects when writing to a timestamptz column. DateTimeOffset sidesteps the
    // Kind ambiguity entirely - convert with .UtcDateTime at the point of use.
    [JsonPropertyName("date")]
    public DateTimeOffset Date { get; set; }

    [JsonPropertyName("status")]
    public required StatusInfo Status { get; set; }
}

public class StatusInfo
{
    // Short codes confirmed live: "1H", "2H", "HT", "FT", "NS" (not started). Longer set documented
    // by API-Football also includes "ET", "P", "PEN", "SUSP", "INT", "PST", "CANC", "ABD", "AWD",
    // "WO" — not yet seen live, map defensively rather than assuming only the above.
    [JsonPropertyName("short")]
    public required string Short { get; set; }

    [JsonPropertyName("elapsed")]
    public int? Elapsed { get; set; }
}

public class LeagueInfo
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("season")]
    public int Season { get; set; }
}

public class TeamsInfo
{
    [JsonPropertyName("home")]
    public required TeamInfo Home { get; set; }

    [JsonPropertyName("away")]
    public required TeamInfo Away { get; set; }
}

public class TeamInfo
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("logo")]
    public string? Logo { get; set; }
}

public class GoalsInfo
{
    [JsonPropertyName("home")]
    public int? Home { get; set; }

    [JsonPropertyName("away")]
    public int? Away { get; set; }
}

// /players/squads?team={id} - confirmed 2026-09-06: player names come back abbreviated
// ("J. Pickford"), unlike the-odds-api's full names ("Jordan Pickford") - see TeamNameMatcher's
// surname-only matching in TestController.PlayerProps for why.
public class SquadDto
{
    [JsonPropertyName("team")]
    public required TeamInfo Team { get; set; }

    [JsonPropertyName("players")]
    public List<SquadPlayerDto> Players { get; set; } = [];
}

public class SquadPlayerDto
{
    [JsonPropertyName("name")]
    public required string Name { get; set; }
}
