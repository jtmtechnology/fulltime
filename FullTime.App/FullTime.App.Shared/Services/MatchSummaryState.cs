using FullTime.App.Shared.Models;

namespace FullTime.App.Shared.Services;

// Scoped, same pattern as BetSlipState - lets MatchSummarySheet render as a same-page overlay
// (see MainLayout.razor) instead of a real page navigation, so opening/closing it never disturbs
// the underlying Matches/LeagueMatches list's scroll position.
public class MatchSummaryState
{
    public UpcomingMatchDto? Match { get; private set; }
    public bool IsOpen { get; private set; }

    public event Action? Changed;

    public void Open(UpcomingMatchDto match)
    {
        Match = match;
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
