using FullTime.App.Shared.Models;

namespace FullTime.App.Shared.Services;

// Known league/cup IDs, keyed on Highlightly's own league IDs (see FullTime.Api's
// HighlightlyLeagueMap.cs, the server-side source of truth for these same values). Reverted back
// to Highlightly 2026-09-06 after a same-day API-Football/the-odds-api cutover attempt - see
// HANDOVER.md for why. Unlike the old provider, Highlightly keeps qualifying/play-off rounds
// under the *same* ID as the main competition and gives every tracked competition, including the
// English lower divisions, a stable ID with its own logo — so there's no temporary-ID remapping
// or shared qualifying-round pool to maintain here any more.
public static class LeagueCatalog
{
    public const long NationsLeague = 5;

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
        // No Highlightly ID exists for this one (added after Highlightly was dropped) - it's stored
        // under API-Football's own ID, see the API's HighlightlyToApiFootballLeagueMap.
        [NationsLeague] = "Nations League",
    };

    // Always shown regardless of preference: the domestic English pyramid only. FA Cup (39079) is
    // now only synced from the 3rd Round Proper onwards — see
    // HighlightlyMatchSyncService.IsEligibleFaCupRound — so it never shows the non-league early
    // rounds that caused the original quota-exhaustion incident.
    public static readonly long[] AlwaysVisible = [33973, 34824, 35675, 36526, 39079, 41632, 450112];

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
        ("Nations League", [NationsLeague]),
    ];

    public static readonly long[] DisplayOrder =
        [.. AlwaysVisible, .. OptionalLeagues.SelectMany(l => l.LeagueIds).Distinct()];

    // Cup/knockout competitions - FA Cup, EFL Cup, Community Shield are pure knockout (see
    // KnockoutOnly below); the UEFA club competitions include a group/league phase but are still
    // cup competitions in the sense that matters for the Match Alerts "Favourite leagues" picker -
    // there's no genuine week-to-week table to follow the way there is for a domestic league.
    private static readonly HashSet<long> CupCompetitionIds =
        [39079, 41632, 450112, 2486, 3337, 722432, NationsLeague];

    // DisplayOrder with cup competitions filtered out - just the domestic top-flight leagues.
    public static readonly long[] LeaguesOnly = [.. DisplayOrder.Where(id => !CupCompetitionIds.Contains(id))];

    // Display-only subtitle for the competition list row (Matches.razor) - not used for any
    // matching/sync logic, so a missing entry just renders no subtitle rather than breaking anything.
    private static readonly Dictionary<long, string> Countries = new()
    {
        [33973] = "England",
        [34824] = "England",
        [35675] = "England",
        [36526] = "England",
        [39079] = "England",
        [41632] = "England",
        [450112] = "England",
        [67162] = "Germany",
        [119924] = "Spain",
        [52695] = "France",
        [115669] = "Italy",
        [2486] = "Europe",
        [3337] = "Europe",
        [722432] = "Europe",
        [NationsLeague] = "Europe",
    };

    // No-op now that every competition (main draw and qualifying alike) lives under one Highlightly
    // ID — kept so callers that group/chip/select by this key don't need to change.
    public static long GroupKey(long leagueId) => leagueId;

    // Pure knockout competitions have no league table at all - hides the "Table" link for these
    // rather than linking to a page that could only ever say "no table available".
    private static readonly HashSet<long> KnockoutOnly = [39079, 41632, 450112]; // FA Cup, EFL Cup, Community Shield

    // The Nations League does have tables, but ~14 small groups of them - the API's standings
    // endpoint only returns the first group, which would show League A Group 1 for every match.
    // Hidden until the Table page supports multiple groups.
    public static bool HasTable(long leagueId) => !KnockoutOnly.Contains(leagueId) && leagueId != NationsLeague;

    // Nations League rounds come back as "League A - 1" (tier, then matchday) - the tier is the only
    // grouping the fixture data carries (the groups within a tier only exist in standings). Null for
    // every other competition, and for Nations League rounds outside the league phase (finals,
    // play-offs), so those render ungrouped exactly as before.
    public static string? Tier(long leagueId, string? round)
    {
        if (leagueId != NationsLeague || round is null || !round.StartsWith("League ", StringComparison.Ordinal))
        {
            return null;
        }

        var dash = round.IndexOf(" - ", StringComparison.Ordinal);
        return dash > 0 ? round[..dash] : round;
    }

    // Ungrouped (null-tier) matches first, then League A..D - ordinal order on "League X" is already
    // tier order.
    public static IEnumerable<IGrouping<string?, UpcomingMatchDto>> GroupByTier(IEnumerable<UpcomingMatchDto> matches) =>
        matches.GroupBy(m => Tier(m.LeagueId, m.Round)).OrderBy(g => g.Key, StringComparer.Ordinal);

    public static string Name(long leagueId) => Names.GetValueOrDefault(leagueId, $"League {leagueId}");

    public static string Country(long leagueId) => Countries.GetValueOrDefault(leagueId, "");

    public static string LogoUrl(long leagueId) => leagueId == NationsLeague
        ? $"https://media.api-sports.io/football/leagues/{leagueId}.png"
        : $"https://highlightly.net/soccer/images/leagues/{leagueId}.png";
}
