using FullTime.App.Shared.Models;

namespace FullTime.App.Shared.Services;

// Scoped, same shape as MatchLeaguePreferences but server-backed (via ApiClient) rather than
// device-local - alert subscriptions have to be visible to the API without anyone having the app
// open, so unlike MatchLeaguePreferences this can't just live in device Preferences/localStorage.
// Loaded once per page via InitializeAsync (mirrors ActiveContextState's pattern) and checked by
// every MatchCard's bell icon from the one cached set rather than a call per card.
public class MatchAlertSubscriptions(ApiClient api)
{
    // Included/excluded are explicit per-match overrides (see MatchAlertSubscription.Included
    // server-side) - excluded always wins over a favourite team/league, included forces alerts on
    // even with no matching favourite. A match in neither set just follows the favourites below.
    private HashSet<Guid> _includedMatchIds = [];
    private HashSet<Guid> _excludedMatchIds = [];
    private HashSet<long> _favouriteTeamIds = [];
    private HashSet<long> _favouriteLeagueIds = [];
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
            _includedMatchIds = subscriptions.IncludedMatchIds.ToHashSet();
            _excludedMatchIds = subscriptions.ExcludedMatchIds.ToHashSet();
            _favouriteTeamIds = subscriptions.FavouriteTeamIds.ToHashSet();
            _favouriteLeagueIds = subscriptions.FavouriteLeagueIds.ToHashSet();
        }
        catch
        {
            // Best-effort - a logged-out/anonymous view just shows every bell as unlit rather than
            // surfacing an error for what's a minor feature.
            _includedMatchIds = [];
            _excludedMatchIds = [];
            _favouriteTeamIds = [];
            _favouriteLeagueIds = [];
        }

        Changed?.Invoke();
    }

    // What MatchCard's bell actually lights up on - an explicit exclusion always wins (silences a
    // match even if its team/league is favourited); otherwise it's lit by an explicit inclusion, a
    // favourited team, or a favourited league, in that order of precedence not mattering since
    // they're just ORed together once exclusion is ruled out.
    public bool IsAlerted(UpcomingMatchDto match)
    {
        if (_excludedMatchIds.Contains(match.Id))
        {
            return false;
        }

        return _includedMatchIds.Contains(match.Id)
            || _favouriteTeamIds.Contains(match.HomeTeamId)
            || _favouriteTeamIds.Contains(match.AwayTeamId)
            || _favouriteLeagueIds.Contains(match.LeagueId);
    }

    // Flips whatever IsAlerted currently reports - tapping a bell lit by a favourite team turns it
    // off via an explicit exclusion (not by touching the favourite itself), and tapping it again
    // turns it back on via an explicit inclusion. Either way the match ends up with a real override
    // on file, not just "no row" - a second tap after that always has something concrete to flip.
    public async Task ToggleAsync(UpcomingMatchDto match)
    {
        var subscribing = !IsAlerted(match);

        // Applied optimistically so the bell flips the instant it's tapped - reverted if the actual
        // server call fails, rather than waiting on a round trip before showing any feedback at all.
        ApplyOverride(match.Id, subscribing);
        Changed?.Invoke();

        try
        {
            await api.SetMatchAlertSubscriptionAsync(match.Id, subscribing);
        }
        catch
        {
            ApplyOverride(match.Id, !subscribing);
            Changed?.Invoke();
        }
    }

    private void ApplyOverride(Guid matchId, bool included)
    {
        if (included)
        {
            _excludedMatchIds.Remove(matchId);
            _includedMatchIds.Add(matchId);
        }
        else
        {
            _includedMatchIds.Remove(matchId);
            _excludedMatchIds.Add(matchId);
        }
    }

    // The Match Alerts settings page reads/writes favourite teams and leagues through these
    // (instead of keeping its own separate copy) so MatchCard's bell - reading this same shared
    // instance - picks up a newly-favourited team/league immediately, not just after an app
    // restart. This was a real bug: the settings page used to call ApiClient directly and track its
    // own local set, so favouriting a league there never reached the cache MatchCard actually reads.
    public bool IsFavouriteTeam(long teamId) => _favouriteTeamIds.Contains(teamId);

    public bool IsFavouriteLeague(long leagueId) => _favouriteLeagueIds.Contains(leagueId);

    public async Task ToggleFavouriteTeamAsync(long teamId)
    {
        var favourite = !_favouriteTeamIds.Contains(teamId);
        if (favourite) _favouriteTeamIds.Add(teamId); else _favouriteTeamIds.Remove(teamId);
        Changed?.Invoke();

        try
        {
            await api.SetFavouriteTeamAsync(teamId, favourite);
        }
        catch
        {
            if (favourite) _favouriteTeamIds.Remove(teamId); else _favouriteTeamIds.Add(teamId);
            Changed?.Invoke();
            throw;
        }
    }

    public async Task ToggleFavouriteLeagueAsync(long leagueId)
    {
        var favourite = !_favouriteLeagueIds.Contains(leagueId);
        if (favourite) _favouriteLeagueIds.Add(leagueId); else _favouriteLeagueIds.Remove(leagueId);
        Changed?.Invoke();

        try
        {
            await api.SetFavouriteLeagueAsync(leagueId, favourite);
        }
        catch
        {
            if (favourite) _favouriteLeagueIds.Remove(leagueId); else _favouriteLeagueIds.Add(leagueId);
            Changed?.Invoke();
            throw;
        }
    }
}
