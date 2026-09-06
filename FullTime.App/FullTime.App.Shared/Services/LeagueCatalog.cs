namespace FullTime.App.Shared.Services;

// Known league/cup IDs, keyed on Highlightly's own league IDs (see FullTime.Api's
// HighlightlyLeagueMap.cs, the server-side source of truth for these same values). Unlike the old
// provider, Highlightly keeps qualifying/play-off rounds under the *same* ID as the main
// competition and gives every tracked competition, including the English lower divisions, a
// stable ID with its own logo — so there's no temporary-ID remapping or shared qualifying-round
// pool to maintain here any more.
public static class LeagueCatalog
{
    public static readonly Dictionary<long, string> Names = new()
    {
        [33973] = "Premier League",
        [34824] = "Championship",
        [35675] = "League One",
        [36526] = "League Two",
        [39079] = "FA Cup",
        [41632] = "EFL Cup",
        [450112] = "Community Shield",
        [67162] = "Bundesliga",
        [119924] = "La Liga",
        [52695] = "Ligue 1",
        [115669] = "Serie A",
        [2486] = "Champions League",
        [3337] = "Europa League",
        [722432] = "Conference League",

        // TEMPORARY 2026-09-06: API-Football's own IDs (see AlwaysVisible) - remove alongside its
        // other temporary entries.
        [39] = "Premier League",
        [481] = "Northern NSW NPL",
        [965] = "AFC U20 Asian Cup",
        [192] = "New South Wales NPL",
    };

    // Always shown regardless of preference: the domestic English pyramid only.
    // FA Cup (39079) re-added 2026-09-06 — see HighlightlyLeagueMap.TrackedLeagueIds for the
    // still-unfixed root cause that could bring the quota-exhaustion incident back.
    // TEMPORARY 2026-09-06: 39/481/965/192 are API-Football's own league IDs (not Highlightly's),
    // added only so FullTime.Api.Sandbox's data renders while ApiConfig points at it for testing -
    // remove alongside reverting ApiConfig.BaseUrl to the real API.
    public static readonly long[] AlwaysVisible =
        [33973, 34824, 35675, 36526, 39079, 41632, 450112, 39, 481, 965, 192];

    // Opt-in: other countries' top flights plus the UEFA club competitions. Order here also
    // controls display order after AlwaysVisible.
    public static readonly (string Name, long[] LeagueIds)[] OptionalLeagues =
    [
        ("Bundesliga", [67162]),
        ("La Liga", [119924]),
        ("Ligue 1", [52695]),
        ("Serie A", [115669]),
        ("Champions League", [2486]),
        ("Europa League", [3337]),
        ("Conference League", [722432]),
    ];

    public static readonly long[] DisplayOrder =
        [.. AlwaysVisible, .. OptionalLeagues.SelectMany(l => l.LeagueIds).Distinct()];

    // No-op now that every competition (main draw and qualifying alike) lives under one Highlightly
    // ID — kept so callers that group/chip/select by this key don't need to change.
    public static long GroupKey(long leagueId) => leagueId;

    public static string Name(long leagueId) => Names.GetValueOrDefault(leagueId, $"League {leagueId}");

    // TEMPORARY 2026-09-06: 39/481/965/192 are API-Football's own league IDs (see AlwaysVisible) -
    // that provider's logo CDN uses a different domain/id-space than Highlightly's, so they need
    // their own branch here. Remove alongside AlwaysVisible's temporary entries.
    public static string LogoUrl(long leagueId) => leagueId is 39 or 481 or 965 or 192
        ? $"https://media.api-sports.io/football/leagues/{leagueId}.png"
        : $"https://highlightly.net/soccer/images/leagues/{leagueId}.png";
}
