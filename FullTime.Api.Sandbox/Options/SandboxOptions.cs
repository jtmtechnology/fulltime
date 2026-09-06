namespace FullTime.Api.Sandbox.Options;

public class ApiFootballOptions
{
    public const string SectionName = "ApiFootball";

    // Direct api-sports.io dashboard key (x-apisports-key header), not a RapidAPI key - the two
    // are different accounts/keys even though API-Football is sold on both. Confirmed 2026-09-06:
    // widgets require this direct channel, and a RapidAPI key already used for Highlightly/the-odds
    // -api came back "not subscribed" against the RapidAPI-hosted version of this same API.
    public required string ApiKey { get; set; }
    public string ApiHost { get; set; } = "v3.football.api-sports.io";
}

public class OddsApiOptions
{
    public const string SectionName = "OddsApi";

    public required string ApiKey { get; set; }
    public string ApiHost { get; set; } = "api.the-odds-api.com";
}
