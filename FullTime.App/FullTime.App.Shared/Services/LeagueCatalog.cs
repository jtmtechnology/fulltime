namespace FullTime.App.Shared.Services;

// Known league/cup IDs, keyed on API-Football's own league IDs (see FullTime.Api's
// ApiFootballLeagueMap.cs, the server-side source of truth for these same values) — looked up via
// its GET /leagues?name=&country= endpoint against the real API, not guessed. Replaces the earlier
// Highlightly-keyed catalog wholesale as part of the API-Football/the-odds-api cutover; there is no
// shared ID space with Highlightly's own league IDs at all.
public static class LeagueCatalog
{
    public static readonly Dictionary<long, string> Names = new()
    {
        [39] = "Premier League",
        [40] = "Championship",
        [41] = "League One",
        [42] = "League Two",
        [45] = "FA Cup",
        [48] = "EFL Cup",
        [528] = "Community Shield",
        [78] = "Bundesliga",
        [140] = "La Liga",
        [61] = "Ligue 1",
        [135] = "Serie A",
        [2] = "Champions League",
        [3] = "Europa League",
        [848] = "Conference League",
    };

    // Always shown regardless of preference: the domestic English pyramid only.
    public static readonly long[] AlwaysVisible = [39, 40, 41, 42, 45, 48, 528];

    // Opt-in: other countries' top flights plus the UEFA club competitions. Order here also
    // controls display order after AlwaysVisible.
    public static readonly (string Name, long[] LeagueIds)[] OptionalLeagues =
    [
        ("Bundesliga", [78]),
        ("La Liga", [140]),
        ("Ligue 1", [61]),
        ("Serie A", [135]),
        ("Champions League", [2]),
        ("Europa League", [3]),
        ("Conference League", [848]),
    ];

    public static readonly long[] DisplayOrder =
        [.. AlwaysVisible, .. OptionalLeagues.SelectMany(l => l.LeagueIds).Distinct()];

    // No-op — every competition (main draw and qualifying alike) lives under one API-Football ID.
    // Kept so callers that group/chip/select by this key don't need to change.
    public static long GroupKey(long leagueId) => leagueId;

    public static string Name(long leagueId) => Names.GetValueOrDefault(leagueId, $"League {leagueId}");

    public static string LogoUrl(long leagueId) => $"https://media.api-sports.io/football/leagues/{leagueId}.png";
}
