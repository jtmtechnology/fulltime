using System.Text.Json.Serialization;

namespace FullTime.Api.OddsApi.Dtos;

public class EventOddsResponse
{
    [JsonPropertyName("response")]
    public EventOddsResult? Response { get; set; }
}

public class EventOddsResult
{
    [JsonPropertyName("odds")]
    public BookmakerOddsDto? Odds { get; set; }
}

public class BookmakerOddsDto
{
    [JsonPropertyName("persistentKey")]
    public string? PersistentKey { get; set; }

    [JsonPropertyName("logoUrl")]
    public string? LogoUrl { get; set; }

    [JsonPropertyName("odds")]
    public OddsMarketsDto? Odds { get; set; }
}

public class OddsMarketsDto
{
    [JsonPropertyName("resolvedOddsMarket")]
    public MarketDto? ResolvedOddsMarket { get; set; }

    [JsonPropertyName("matchfactMarkets")]
    public List<MarketDto> MatchfactMarkets { get; set; } = [];
}

public class MarketDto
{
    [JsonPropertyName("headerTranslationKey")]
    public string? HeaderTranslationKey { get; set; }

    [JsonPropertyName("selections")]
    public List<SelectionDto> Selections { get; set; } = [];
}

public class SelectionDto
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("oddsDecimal")]
    public string? OddsDecimal { get; set; }
}
