namespace FullTime.App.Shared.Services;

// Scoped, same pattern as MatchSummaryState - lets StandingsSheet render as a same-page overlay
// (see MainLayout.razor) instead of a real page navigation, so opening/closing it never re-runs
// LeagueMatches.razor's OnInitializedAsync (and the match-list refetch that came with it).
public class StandingsState
{
    public long LeagueId { get; private set; }
    public bool IsOpen { get; private set; }

    public event Action? Changed;

    public void Open(long leagueId)
    {
        LeagueId = leagueId;
        IsOpen = true;
        Changed?.Invoke();
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        Changed?.Invoke();
    }
}
