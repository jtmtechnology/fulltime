namespace FullTime.Api.BetBuilder.OddsApi;

public class OddsApiOptions
{
    public const string SectionName = "OddsApi";

    public required string ApiKey { get; set; }
    public string ApiHost { get; set; } = "api.the-odds-api.com";

    // Freshness tiers by proximity to kickoff — lines barely move days out, but the last hour
    // before kickoff is when prices actually move. Never refetch once a match is no longer
    // Upcoming (see OddsApiMarketService) — nobody can act on stale odds after kickoff anyway
    // (BetService.PlaceBetAsync blocks it), which bounds total exposure per match to its Upcoming
    // window, not indefinitely.
    public int FarTtlHours { get; set; } = 6;
    public int NearTtlMinutes { get; set; } = 45;
    public int ImminentTtlMinutes { get; set; } = 15;

    // Boundaries for the tiers above: "far" is anything beyond this many hours to kickoff, "near"
    // is between imminent and far, "imminent" is inside this many hours to kickoff.
    public int NearBoundaryHours { get; set; } = 24;
    public int ImminentBoundaryHours { get; set; } = 1;

    // A match that's never fully priced (e.g. a lower-league fixture no bookmaker covers) shouldn't
    // get re-hit on every single view forever — cap attempts per match rather than retry
    // indefinitely once every required market has had a real chance to appear.
    public int MaxFetchAttemptsPerMatch { get; set; } = 8;
}
