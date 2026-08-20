namespace FullTime.App.Shared.Services;

// Scoped, same lifetime pattern as ActiveContextState. Holds which optional leagues (beyond the
// always-visible English pyramid) the user wants to see in Matches. Enabled state is keyed by each
// LeagueCatalog.OptionalLeagues group's first (primary) league ID.
public class MatchLeaguePreferences(IMatchLeaguePreferenceStore store)
{
    public HashSet<long> EnabledOptionalLeagueIds { get; private set; } = [];

    public event Action? Changed;

    public async Task InitializeAsync()
    {
        var stored = await store.GetAsync();
        EnabledOptionalLeagueIds = string.IsNullOrEmpty(stored)
            ? []
            : stored.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(long.Parse).ToHashSet();
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
