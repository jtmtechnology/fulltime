namespace FullTime.App.Shared.Services;

// Scoped, same lifetime pattern as ActiveContextState. Holds which optional country leagues
// (beyond the always-visible English leagues + UEFA cups) the user wants to see in Matches.
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

    public async Task SetEnabledAsync(long leagueId, bool enabled)
    {
        if (enabled)
        {
            EnabledOptionalLeagueIds.Add(leagueId);
        }
        else
        {
            EnabledOptionalLeagueIds.Remove(leagueId);
        }

        await store.SetAsync(string.Join(',', EnabledOptionalLeagueIds));
        Changed?.Invoke();
    }

    public bool IsVisible(long leagueId) =>
        LeagueCatalog.AlwaysVisible.Contains(leagueId) || EnabledOptionalLeagueIds.Contains(leagueId);
}
