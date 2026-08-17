using FullTime.App.Shared.Models;

namespace FullTime.App.Shared.Services;

public record BettingContext(Guid? LeagueId, string Name);

// Scoped, same lifetime pattern as AuthState/BetSlipState. Holds which pool bets are currently
// placed against — the global "Worldwide" balance, or a specific private league's own balance —
// since each pool now tracks fully independent bets/balance. The header context switcher and
// BetSlipSheet both read from this.
public class ActiveContextState(IActiveContextStore store, ApiClient api)
{
    public static readonly BettingContext Worldwide = new(null, "Worldwide");

    public BettingContext Current { get; private set; } = Worldwide;
    public decimal? Balance { get; private set; }
    public List<LeagueSummaryDto> MyLeagues { get; private set; } = [];

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
        Current = Guid.TryParse(storedId, out var leagueId) && MyLeagues.FirstOrDefault(l => l.Id == leagueId) is { } league
            ? new BettingContext(league.Id, league.Name)
            : Worldwide;

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
                Balance = (await api.GetMeAsync()).Balance;
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
