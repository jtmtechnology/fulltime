using System.Text.Json;

namespace FullTime.App.Shared.Services;

public record SlipPick(string MarketType, decimal? Line, string? Side, decimal Odds, int? PredictedHomeScore = null, int? PredictedAwayScore = null);

public record SlipLeg(List<SlipPick> Picks, string HomeTeam, string AwayTeam, DateTime KickoffTime)
{
    public decimal CombinedOdds => Picks.Aggregate(1m, (acc, p) => acc * p.Odds);
}

// Scoped, same as AuthState/ApiClient. Replaces the old JS `slip` Map — persisted through
// ISlipStore so it survives a page reload, same as the old localStorage-backed behavior.
//
// Keyed by MatchId, same as the original single-pick slip — a same-game multi (bet builder leg)
// is still just one leg per match, now allowed to carry more than one market pick.
public class BetSlipState(ISlipStore slipStore)
{
    private Dictionary<Guid, SlipLeg> _legs = [];

    public IReadOnlyDictionary<Guid, SlipLeg> Legs => _legs;
    public decimal CombinedOdds => _legs.Values.Aggregate(1m, (acc, l) => acc * l.CombinedOdds);

    // UI-only concern, but BetSlipSheet and BottomTabBar both need to agree on whether the
    // slip sheet is expanded, and this is the service they already share for that purpose.
    public bool IsOpen { get; private set; }

    public event Action? Changed;

    public void SetOpen(bool open)
    {
        if (IsOpen == open) return;
        IsOpen = open;
        Changed?.Invoke();
    }

    public void ToggleOpen() => SetOpen(!IsOpen);

    public async Task InitializeAsync()
    {
        var json = await slipStore.GetAsync();
        if (json is null) return;

        try
        {
            _legs = JsonSerializer.Deserialize<Dictionary<Guid, SlipLeg>>(json) ?? [];
        }
        catch
        {
            _legs = [];
        }
    }

    // Used by OddsCell to highlight the currently-selected 1X2 pill — only meaningful for a plain
    // single-pick MatchResult leg, since a bet-builder leg has no one "side" to highlight.
    public string? GetPick(Guid matchId)
    {
        if (!_legs.TryGetValue(matchId, out var leg) || leg.Picks.Count != 1)
        {
            return null;
        }

        var pick = leg.Picks[0];
        return pick.MarketType == "MatchResult" ? pick.Side : null;
    }

    public async Task ToggleMatchResultAsync(
        Guid matchId, string side, decimal odds, string homeTeam, string awayTeam, DateTime kickoffTime)
    {
        if (GetPick(matchId) == side)
        {
            _legs.Remove(matchId);
        }
        else
        {
            _legs[matchId] = new SlipLeg([new SlipPick("MatchResult", null, side, odds)], homeTeam, awayTeam, kickoffTime);
        }

        await SaveAsync();
    }

    // Adds a same-game multi leg (or replaces whatever leg already exists for this match) —
    // same one-leg-per-match rule as ToggleMatchResultAsync, just with 2+ picks.
    public async Task SetLegAsync(Guid matchId, List<SlipPick> picks, string homeTeam, string awayTeam, DateTime kickoffTime)
    {
        _legs[matchId] = new SlipLeg(picks, homeTeam, awayTeam, kickoffTime);
        await SaveAsync();
    }

    public async Task RemoveAsync(Guid matchId)
    {
        _legs.Remove(matchId);
        await SaveAsync();
    }

    public async Task ClearAsync()
    {
        _legs.Clear();
        await SaveAsync();
    }

    private async Task SaveAsync()
    {
        await slipStore.SetAsync(JsonSerializer.Serialize(_legs));
        Changed?.Invoke();
    }
}
