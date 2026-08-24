using FullTime.App.Shared.Models;

namespace FullTime.App.Shared.Services;

public record BettingContext(Guid? LeagueId, string Name);

// Scoped, same lifetime pattern as AuthState/BetSlipState. Holds which league's balance bets are
// currently placed against — every bet needs one, there's no standalone Worldwide pool any more
// (see BetService.PlaceBetAsync and LeaderboardController, where "Worldwide" is now just a computed
// average across a user's leagues, not something you can bet against directly). The header context
// switcher and BetSlipSheet both read from this.
public class ActiveContextState(IActiveContextStore store, ApiClient api)
{
    // Sentinel for "nothing to bet with yet" — a user who hasn't joined or created a league.
    public static readonly BettingContext NoLeagues = new(null, "No leagues yet");

    public BettingContext Current { get; private set; } = NoLeagues;
    public decimal? Balance { get; private set; }
    public List<LeagueSummaryDto> MyLeagues { get; private set; } = [];

    // Every LeagueSummaryDto is about the current user's own membership, so any of them carries the
    // same CurrencySymbol - falls back to £ before the first league loads, same default the API
    // itself uses for a user with no country set.
    public string CurrencySymbol => MyLeagues.FirstOrDefault()?.CurrencySymbol ?? "£";

    // UI-only, but this is the service the trigger button and the sheet both already share —
    // same reasoning as BetSlipState.IsOpen.
    public bool IsSheetOpen { get; private set; }

    public event Action? Changed;

    public void SetSheetOpen(bool open)
    {
        if (IsSheetOpen == open) return;
        IsSheetOpen = open;
        Changed?.Invoke();
    }

    public void ToggleSheetOpen() => SetSheetOpen(!IsSheetOpen);

    public async Task InitializeAsync()
    {
        await RefreshLeaguesAsync();

        var storedId = await store.GetAsync();
        var storedLeague = Guid.TryParse(storedId, out var leagueId)
            ? MyLeagues.FirstOrDefault(l => l.Id == leagueId)
            : null;

        // Default to the first league rather than a standalone Worldwide pool — a single-league
        // user shouldn't have to open the switcher just to bet in the only league they're in.
        var league = storedLeague ?? MyLeagues.FirstOrDefault();
        Current = league is not null ? new BettingContext(league.Id, league.Name) : NoLeagues;

        await RefreshBalanceAsync();
    }

    public async Task RefreshLeaguesAsync()
    {
        try
        {
            MyLeagues = await api.GetMyLeaguesAsync();
        }
        catch
        {
            // best-effort — the switcher just won't offer any leagues if this fails
        }
    }

    public async Task SetContextAsync(BettingContext context)
    {
        Current = context;
        await store.SetAsync(context.LeagueId?.ToString() ?? "");
        await RefreshBalanceAsync();
    }

    public async Task RefreshBalanceAsync()
    {
        try
        {
            if (Current.LeagueId is null)
            {
                // NoLeagues sentinel — nothing to bet with, so nothing to show a balance for.
                Balance = null;
            }
            else
            {
                await RefreshLeaguesAsync();
                Balance = MyLeagues.FirstOrDefault(l => l.Id == Current.LeagueId)?.Balance;
            }
        }
        catch
        {
            // balance display is a nice-to-have, not worth surfacing a network error for
        }

        Changed?.Invoke();
    }
}
