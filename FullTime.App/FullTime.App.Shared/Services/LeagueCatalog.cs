namespace FullTime.App.Shared.Services;

// Known league/cup IDs from the odds provider. Some divisions are tagged under a provider-assigned
// temporary ID until it links them to the canonical one — both are listed where that applies.
public static class LeagueCatalog
{
    public static readonly Dictionary<long, string> Names = new()
    {
        [47] = "Premier League",
        [48] = "Championship",
        [938218] = "Championship",
        [108] = "League One",
        [938219] = "League One",
        [109] = "League Two",
        [938220] = "League Two",
        [132] = "FA Cup",
        [133] = "EFL Cup",
        [938221] = "EFL Cup",
        [247] = "Community Shield",
        [42] = "Champions League",
        [10611] = "Champions League",
        [73] = "Europa League",
        [10613] = "Europa League",
        [10216] = "Conference League",
        [10615] = "Conference League",
        [54] = "Bundesliga",
        [87] = "La Liga",
        [53] = "Ligue 1",
        [55] = "Serie A",
    };

    // The provider's temporary per-season league IDs, and separate qualification-round IDs for the
    // UEFA cups (see Names above), have no logo asset of their own yet — fall back to the canonical
    // competition's logo, which does.
    public static readonly Dictionary<long, long> LogoId = new()
    {
        [938218] = 48,
        [938219] = 108,
        [938220] = 109,
        [938221] = 133,
        [10611] = 42,
        [10613] = 73,
        [10615] = 10216,
    };

    // Always shown regardless of preference: the domestic English pyramid only.
    public static readonly long[] AlwaysVisible =
        [47, 48, 938218, 108, 938219, 109, 938220, 132, 133, 938221, 247];

    // Opt-in: other countries' top flights plus the UEFA club competitions. Each entry's LeagueIds
    // covers every stage of that competition — qualifying and play-off rounds included, not just the
    // main draw — so enabling "Europa League" shows the whole competition, not only the group stage.
    // Order here also controls display order after AlwaysVisible.
    public static readonly (string Name, long[] LeagueIds)[] OptionalLeagues =
    [
        ("Bundesliga", [54]),
        ("La Liga", [87]),
        ("Ligue 1", [53]),
        ("Serie A", [55]),
        ("Champions League", [42, 10611]),
        ("Europa League", [73, 10613]),
        ("Conference League", [10216, 10615]),
    ];

    public static readonly long[] DisplayOrder = [.. AlwaysVisible, .. OptionalLeagues.SelectMany(l => l.LeagueIds)];

    public static string Name(long leagueId) => Names.GetValueOrDefault(leagueId, $"League {leagueId}");

    public static string LogoUrl(long leagueId)
    {
        var id = LogoId.GetValueOrDefault(leagueId, leagueId);
        return $"https://images.fotmob.com/image_resources/logo/leaguelogo/{id}.png";
    }
}
