using System.Text.Json;

namespace FullTime.App.Shared.Services;

public record SlipSelection(string Pick, decimal Odds, string HomeTeam, string AwayTeam, DateTime KickoffTime);

// Scoped, same as AuthState/ApiClient. Replaces the old JS `slip` Map — persisted through
// ISlipStore so it survives a page reload, same as the old localStorage-backed behavior.
public class BetSlipState(ISlipStore slipStore)
{
    private Dictionary<Guid, SlipSelection> _selections = [];

    public IReadOnlyDictionary<Guid, SlipSelection> Selections => _selections;
    public decimal CombinedOdds => _selections.Values.Aggregate(1m, (acc, s) => acc * s.Odds);

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
            _selections = JsonSerializer.Deserialize<Dictionary<Guid, SlipSelection>>(json) ?? [];
        }
        catch
        {
            _selections = [];
        }
    }

    public string? GetPick(Guid matchId) => _selections.TryGetValue(matchId, out var s) ? s.Pick : null;

    public async Task ToggleAsync(Guid matchId, string pick, decimal odds, string homeTeam, string awayTeam, DateTime kickoffTime)
    {
        if (_selections.TryGetValue(matchId, out var existing) && existing.Pick == pick)
        {
            _selections.Remove(matchId);
        }
        else
        {
            _selections[matchId] = new SlipSelection(pick, odds, homeTeam, awayTeam, kickoffTime);
        }

        await SaveAsync();
    }

    public async Task RemoveAsync(Guid matchId)
    {
        _selections.Remove(matchId);
        await SaveAsync();
    }

    public async Task ClearAsync()
    {
        _selections.Clear();
        await SaveAsync();
    }

    private async Task SaveAsync()
    {
        await slipStore.SetAsync(JsonSerializer.Serialize(_selections));
        Changed?.Invoke();
    }
}
