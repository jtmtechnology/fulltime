namespace FullTime.Api.Sandbox.Models;

// One priced player-prop outcome pulled from the-odds-api, tied to one of our own SandboxMatch
// rows (identified via API-Football). There is no shared ID between the-odds-api and API-Football
// - linking these two always goes through TeamNameMatcher, never a direct foreign key from the
// provider's own event ID.
public class SandboxPlayerPropMarket
{
    public Guid Id { get; set; }
    public Guid MatchId { get; set; }
    public SandboxMatch? Match { get; set; }

    // e.g. "player_goal_scorer_anytime", "player_assists" - kept as a raw string rather than an
    // enum here, since this project exists to find out which market keys are actually worth
    // building real MarketType enum values for in FullTime.Api.
    public required string MarketKey { get; set; }

    // Null for match-level markets (h2h, totals, btts, corners - see TestController.MatchMarkets) -
    // this table doubles up as storage for both those and per-player props (TestController.
    // PlayerProps) rather than duplicating a near-identical table, since both are just "one priced
    // the-odds-api outcome for this match."
    public string? PlayerName { get; set; }

    // "Home"/"Away", resolved by matching PlayerName's surname against API-Football's squad list
    // for each team (see TestController.PlayerProps) - the-odds-api gives no team/roster data of
    // its own, and its player names ("Jordan Pickford") don't match API-Football's own format
    // ("J. Pickford") closely enough to compare directly, hence surname-only matching. Null if no
    // squad member's surname matched either team (e.g. a nickname the surname heuristic misses).
    public string? Team { get; set; }

    // The outcome's own "name" - "Yes" for goalscorer/card markets, "Over"/"Under" for the
    // line-based props (shots, assists). Needed to render/select the right cell - two different
    // players can share the same Line, so Line alone doesn't disambiguate an Over from an Under.
    // Null only for MarketKey "correct_score", which has no meaningful "side" for one specific
    // scoreline (see PredictedHomeScore/PredictedAwayScore instead).
    public string? Side { get; set; }
    public required string Bookmaker { get; set; }
    public decimal Price { get; set; }
    public decimal? Point { get; set; }

    // Only set for MarketKey "correct_score" - the-odds-api packs both scores into one outcome
    // name ("Everton:1|Manchester United:0"), parsed in TestController.MatchMarkets.
    public int? PredictedHomeScore { get; set; }
    public int? PredictedAwayScore { get; set; }
    public DateTime FetchedAt { get; set; }
}
