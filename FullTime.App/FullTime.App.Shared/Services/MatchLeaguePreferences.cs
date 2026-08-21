namespace FullTime.App.Shared.Services;

// Scoped, same lifetime pattern as ActiveContextState. Holds which optional leagues (beyond the
// always-visible English pyramid) the user wants to see in Matches. Enabled state is keyed by each
// LeagueCatalog.OptionalLeagues group's first (primary) league ID.
public class MatchLeaguePreferences(IMatchLeaguePreferenceStore store)
{
    // One-time translation for preferences stored before the switch to Highlightly as the sole
    // data source — old provider's league ID -> Highlightly's own ID for the same competition
    // (see FullTime.Api's HighlightlyLeagueMap.cs). Without this, an existing user's stored toggles
    // would silently stop matching anything once LeagueCatalog re-keyed to Highlightly's IDs.
    private static readonly Dictionary<long, long> LegacyIdTranslation = new()
    {
        [54] = 67162,
        [87] = 119924,
        [53] = 52695,
        [55] = 115669,
        [42] = 2486,
        [73] = 3337,
        [10216] = 722432,
    };

    public HashSet<long> EnabledOptionalLeagueIds { get; private set; } = [];

    public event Action? Changed;

    public async Task InitializeAsync()
    {
        var stored = await store.GetAsync();
        var parsed = string.IsNullOrEmpty(stored)
            ? []
            : stored.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(long.Parse).ToHashSet();

        var translated = parsed.Select(id => LegacyIdTranslation.GetValueOrDefault(id, id)).ToHashSet();
        EnabledOptionalLeagueIds = translated;

        if (!translated.SetEquals(parsed))
        {
            await store.SetAsync(string.Join(',', translated));
        }

        Changed?.Invoke();
    }

    public async Task SetEnabledAsync(long groupPrimaryId, bool enabled)
    {
        if (enabled)
        {
            EnabledOptionalLeagueIds.Add(groupPrimaryId);
        }
        else
        {
            EnabledOptionalLeagueIds.Remove(groupPrimaryId);
        }

        await store.SetAsync(string.Join(',', EnabledOptionalLeagueIds));
        Changed?.Invoke();
    }

    public bool IsVisible(long leagueId)
    {
        if (LeagueCatalog.AlwaysVisible.Contains(leagueId))
        {
            return true;
        }

        // A qualifying-round ID can belong to more than one cup's group (see LeagueCatalog) — it's
        // visible if ANY group containing it is enabled, not just the first match.
        return LeagueCatalog.OptionalLeagues
            .Where(g => g.LeagueIds.Contains(leagueId))
            .Any(g => EnabledOptionalLeagueIds.Contains(g.LeagueIds[0]));
    }
}
