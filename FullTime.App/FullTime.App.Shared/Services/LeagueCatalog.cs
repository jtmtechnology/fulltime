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
    };

    // Always shown regardless of preference: the domestic English pyramid only.
    public static readonly long[] AlwaysVisible =
        [33973, 34824, 35675, 36526, 39079, 41632, 450112];

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

    public static string LogoUrl(long leagueId) => $"https://highlightly.net/soccer/images/leagues/{leagueId}.png";
}
