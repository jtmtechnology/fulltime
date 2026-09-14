namespace FullTime.App.Shared.Services;

// Scoped, same shape as MatchLeaguePreferences but server-backed (via ApiClient) rather than
// device-local - alert subscriptions have to be visible to the API without anyone having the app
// open, so unlike MatchLeaguePreferences this can't just live in device Preferences/localStorage.
// Loaded once per page via InitializeAsync (mirrors ActiveContextState's pattern) and checked by
// every MatchCard's bell icon from the one cached set rather than a call per card.
public class MatchAlertSubscriptions(ApiClient api)
{
    private HashSet<Guid> _subscribedMatchIds = [];
    private bool _initialized;

    public event Action? Changed;

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        try
        {
            var subscriptions = await api.GetAlertSubscriptionsAsync();
            _subscribedMatchIds = subscriptions.SubscribedMatchIds.ToHashSet();
        }
        catch
        {
            // Best-effort - a logged-out/anonymous view just shows every bell as unlit rather than
            // surfacing an error for what's a minor feature.
            _subscribedMatchIds = [];
        }

        Changed?.Invoke();
    }

    public bool IsSubscribed(Guid matchId) => _subscribedMatchIds.Contains(matchId);

    public async Task ToggleAsync(Guid matchId)
    {
        var subscribing = !_subscribedMatchIds.Contains(matchId);

        // Applied optimistically so the bell flips the instant it's tapped - reverted if the actual
        // server call fails, rather than waiting on a round trip before showing any feedback at all.
        if (subscribing)
        {
            _subscribedMatchIds.Add(matchId);
        }
        else
        {
            _subscribedMatchIds.Remove(matchId);
        }

        Changed?.Invoke();

        try
        {
            await api.SetMatchAlertSubscriptionAsync(matchId, subscribing);
        }
        catch
        {
            if (subscribing)
            {
                _subscribedMatchIds.Remove(matchId);
            }
            else
            {
                _subscribedMatchIds.Add(matchId);
            }

            Changed?.Invoke();
        }
    }
}
