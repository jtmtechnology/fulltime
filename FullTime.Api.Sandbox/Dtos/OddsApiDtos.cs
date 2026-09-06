using System.Text.Json.Serialization;

namespace FullTime.Api.Sandbox.Dtos;

// Shapes confirmed against real responses from api.the-odds-api.com/v4 on 2026-09-06 — see
// HANDOVER.md for the manual curl session this was built from.
public class OddsApiSportDto
{
    [JsonPropertyName("key")]
    public required string Key { get; set; }

    [JsonPropertyName("title")]
    public required string Title { get; set; }

    [JsonPropertyName("active")]
    public bool Active { get; set; }
}

public class OddsApiEventDto
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    [JsonPropertyName("sport_key")]
    public required string SportKey { get; set; }

    [JsonPropertyName("commence_time")]
    public DateTime CommenceTime { get; set; }

    [JsonPropertyName("home_team")]
    public required string HomeTeam { get; set; }

    [JsonPropertyName("away_team")]
    public required string AwayTeam { get; set; }
}

public class OddsApiEventOddsDto
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    [JsonPropertyName("home_team")]
    public required string HomeTeam { get; set; }

    [JsonPropertyName("away_team")]
    public required string AwayTeam { get; set; }

    [JsonPropertyName("bookmakers")]
    public List<BookmakerDto> Bookmakers { get; set; } = [];
}

public class BookmakerDto
{
    [JsonPropertyName("key")]
    public required string Key { get; set; }

    [JsonPropertyName("title")]
    public required string Title { get; set; }

    [JsonPropertyName("markets")]
    public List<MarketDto> Markets { get; set; } = [];
}

public class MarketDto
{
    [JsonPropertyName("key")]
    public required string Key { get; set; }

    [JsonPropertyName("outcomes")]
    public List<OutcomeDto> Outcomes { get; set; } = [];
}

public class OutcomeDto
{
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    // Only present on player-prop markets - the player's name (the market's own outer "name" is
    // "Yes"/"Over"/etc. there, not the selection itself - confirmed against a real
    // player_goal_scorer_anytime response where every outcome's "name" was "Yes").
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("price")]
    public decimal Price { get; set; }

    [JsonPropertyName("point")]
    public decimal? Point { get; set; }
}
