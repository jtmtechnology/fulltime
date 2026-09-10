using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FullTime.Api.BetBuilder.Dtos;

// Confirmed live 2026-09-10: at least one bet type's "value" comes back as a raw JSON number
// rather than a quoted string (crashed deserialization for the whole fixture's odds response
// before this - "Cannot get the value of a token type 'Number' as a string"). Tolerates either
// shape rather than assuming every bet type is string-valued.
public class FlexibleStringConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.Number
            ? reader.GetDouble().ToString(CultureInfo.InvariantCulture)
            : reader.GetString() ?? "";

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}

// Shapes confirmed against real responses from v3.football.api-sports.io — ported from
// FullTime.Api.Sandbox/Dtos/ApiFootballDtos.cs, which validated these against the real API during
// evaluation. Only the fields the sync/settlement code actually uses are mapped.
public class ApiFootballResponse<T>
{
    [JsonPropertyName("results")]
    public int Results { get; set; }

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

    // DateTimeOffset, not DateTime — api-football sends an explicit "+00:00" offset (not "Z"), and
    // System.Text.Json's default DateTime converter parses that as Kind=Local rather than Utc,
    // which Npgsql then rejects when writing to a timestamptz column. Convert with .UtcDateTime at
    // the point of use.
    [JsonPropertyName("date")]
    public DateTimeOffset Date { get; set; }

    [JsonPropertyName("status")]
    public required StatusInfo Status { get; set; }
}

public class StatusInfo
{
    // Short codes confirmed live: "1H", "2H", "HT", "FT", "NS". Documented set also includes "ET",
    // "P", "PEN", "SUSP", "INT", "PST", "CANC", "ABD", "AWD", "WO" — mapped defensively (see
    // ApiFootballMatchSyncService.DeriveStatus) rather than assuming only the confirmed ones.
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

    // e.g. "1st Round Qualifying", "3rd Round Proper", "Quarter-finals" - only meaningful for
    // knockout competitions (FA Cup, EFL Cup, etc.); null/unused for league competitions. See
    // ApiFootballMatchSyncService.IsEligibleFaCupRound for why this matters for FA Cup specifically.
    [JsonPropertyName("round")]
    public string? Round { get; set; }
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

// /players/squads?team={id} — player names come back abbreviated ("J. Pickford"), unlike
// the-odds-api's full names ("Jordan Pickford") — see OddsApi's surname-only matching.
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

// /fixtures/events?fixture={id} — full match event timeline (goals/cards/subs/VAR), same role as
// Highlightly's MatchEventDto (see ApiFootballSettlementSupportService.ResolveMatchEventsAsync).
// Player/Assist/Detail confirmed live 2026-09-10 against a real finished fixture — "assist" is
// always present as an object even with no assist, just {"id": null, "name": null}, hence
// EventPlayerInfo's fields are nullable rather than reusing the stricter PlayerInfo used elsewhere.
public class FixtureEventDto
{
    [JsonPropertyName("time")]
    public required EventTimeDto Time { get; set; }

    [JsonPropertyName("team")]
    public required TeamInfo Team { get; set; }

    [JsonPropertyName("player")]
    public required EventPlayerInfo Player { get; set; }

    [JsonPropertyName("assist")]
    public EventPlayerInfo? Assist { get; set; }

    [JsonPropertyName("type")]
    public required string Type { get; set; }

    [JsonPropertyName("detail")]
    public required string Detail { get; set; }
}

public class EventPlayerInfo
{
    [JsonPropertyName("id")]
    public long? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public class EventTimeDto
{
    [JsonPropertyName("elapsed")]
    public int Elapsed { get; set; }

    [JsonPropertyName("extra")]
    public int? Extra { get; set; }
}

// /fixtures/players?fixture={id} — per-player post-match stats, confirmed real via a spike against
// a finished fixture 2026-09-06 (goals/assists/shots-on-target/yellow cards all populated). Powers
// settlement for PlayerGoalscorerAnytime/PlayerCard/PlayerShotsOnTarget/PlayerAssists.
public class FixturePlayersResponseTeam
{
    [JsonPropertyName("team")]
    public required TeamInfo Team { get; set; }

    [JsonPropertyName("players")]
    public List<FixturePlayerEntry> Players { get; set; } = [];
}

public class FixturePlayerEntry
{
    [JsonPropertyName("player")]
    public required PlayerInfo Player { get; set; }

    [JsonPropertyName("statistics")]
    public List<PlayerMatchStatistics> Statistics { get; set; } = [];
}

public class PlayerInfo
{
    [JsonPropertyName("name")]
    public required string Name { get; set; }
}

public class PlayerMatchStatistics
{
    [JsonPropertyName("shots")]
    public ShotsStat? Shots { get; set; }

    [JsonPropertyName("goals")]
    public GoalsStat? Goals { get; set; }

    [JsonPropertyName("cards")]
    public CardsStat? Cards { get; set; }

    [JsonPropertyName("fouls")]
    public FoulsStat? Fouls { get; set; }
}

public class FoulsStat
{
    [JsonPropertyName("committed")]
    public int? Committed { get; set; }
}

public class ShotsStat
{
    [JsonPropertyName("on")]
    public int? On { get; set; }

    [JsonPropertyName("total")]
    public int? Total { get; set; }
}

public class GoalsStat
{
    [JsonPropertyName("total")]
    public int? Total { get; set; }

    [JsonPropertyName("assists")]
    public int? Assists { get; set; }
}

public class CardsStat
{
    [JsonPropertyName("yellow")]
    public int? Yellow { get; set; }

    [JsonPropertyName("red")]
    public int? Red { get; set; }
}

// /fixtures/statistics?fixture={id} — team-level match stats, used only for total corners (summed
// across both teams). Confirmed real via the same spike ("Corner Kicks" populated for both sides).
public class FixtureStatisticsTeam
{
    [JsonPropertyName("team")]
    public required TeamInfo Team { get; set; }

    [JsonPropertyName("statistics")]
    public List<FixtureStatisticEntry> Statistics { get; set; } = [];
}

public class FixtureStatisticEntry
{
    [JsonPropertyName("type")]
    public required string Type { get; set; }

    // Comes back as a number, a percentage string ("54%"), or null depending on Type — only ever
    // read for Type == "Corner Kicks"/"Yellow Cards"/"Red Cards" (plain ints, "Red Cards" can be
    // null when zero — confirmed live 2026-09-10), so a JsonElement sidesteps modelling every
    // other stat's shape just to ignore it.
    [JsonPropertyName("value")]
    public System.Text.Json.JsonElement Value { get; set; }
}

// /odds?fixture={id}&bookmaker={id} — confirmed live 2026-09-10 against a real upcoming fixture:
// a single call returns every market Bet365 currently prices for that fixture, one response item
// (we only ever request one fixture at a time, so .Response has at most one entry).
public class OddsFixtureResponseDto
{
    [JsonPropertyName("bookmakers")]
    public List<BookmakerOddsDto> Bookmakers { get; set; } = [];
}

public class BookmakerOddsDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("bets")]
    public List<BetOddsDto> Bets { get; set; } = [];
}

public class BetOddsDto
{
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("values")]
    public List<BetValueDto> Values { get; set; } = [];
}

public class BetValueDto
{
    [JsonPropertyName("value")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public required string Value { get; set; }

    [JsonPropertyName("odd")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public required string Odd { get; set; }
}
