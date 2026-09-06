namespace FullTime.App.Shared.Services;

// Scoped, same lifetime pattern as ActiveContextState. Holds which optional leagues (beyond the
// always-visible English pyramid) the user wants to see in Matches. Enabled state is keyed by each
// LeagueCatalog.OptionalLeagues group's first (primary) league ID.
public class MatchLeaguePreferences(IMatchLeaguePreferenceStore store)
{
    // Two chained one-time translations for preferences stored under an earlier provider's league
    // IDs — old provider -> Highlightly, then Highlightly -> API-Football (the current provider,
    // see FullTime.Api's ApiFootballLeagueMap.cs). Without this, an existing user's stored toggles
    // would silently stop matching anything each time LeagueCatalog re-keys to a new provider's IDs.
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

    private static readonly Dictionary<long, long> HighlightlyToApiFootballTranslation = new()
    {
        [33973] = 39,
        [34824] = 40,
        [35675] = 41,
        [36526] = 42,
        [39079] = 45,
        [41632] = 48,
        [450112] = 528,
        [67162] = 78,
        [119924] = 140,
        [52695] = 61,
        [115669] = 135,
        [2486] = 2,
        [3337] = 3,
        [722432] = 848,
    };

    public HashSet<long> EnabledOptionalLeagueIds { get; private set; } = [];

    public event Action? Changed;

    public async Task InitializeAsync()
    {
        var stored = await store.GetAsync();
        var parsed = string.IsNullOrEmpty(stored)
            ? []
            : stored.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(long.Parse).ToHashSet();

        var translated = parsed
            .Select(id => LegacyIdTranslation.GetValueOrDefault(id, id))
            .Select(id => HighlightlyToApiFootballTranslation.GetValueOrDefault(id, id))
            .ToHashSet();
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
