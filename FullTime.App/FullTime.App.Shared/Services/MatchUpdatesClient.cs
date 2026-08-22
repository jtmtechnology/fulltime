using Microsoft.AspNetCore.SignalR.Client;

namespace FullTime.App.Shared.Services;

public record MatchLiveUpdate(Guid MatchId, int? HomeScore, int? AwayScore, string Status, int? Minute, bool IsHalfTime);

// Scoped, same lifetime as the other per-circuit (Web)/per-session (MAUI) services. Opens one
// persistent SignalR connection directly to FullTime.Api's hub — the same host ApiClient already
// talks to over plain HTTP — so a live score change reaches Matches.razor immediately instead of
// waiting for its own next poll. Safe to call EnsureStartedAsync from multiple pages; only the
// first call actually opens a connection.
public class MatchUpdatesClient(ApiClient api) : IAsyncDisposable
{
    private HubConnection? _connection;

    public event Action<MatchLiveUpdate>? MatchUpdated;

    public async Task EnsureStartedAsync()
    {
        if (_connection is not null)
        {
            return;
        }

        var baseAddress = api.BaseAddress
            ?? throw new InvalidOperationException("ApiClient has no BaseAddress configured.");

        _connection = new HubConnectionBuilder()
            .WithUrl(new Uri(baseAddress, "hubs/matches"))
            .WithAutomaticReconnect()
            .Build();

        _connection.On<MatchLiveUpdate>("MatchUpdated", update => MatchUpdated?.Invoke(update));

        await _connection.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
    }
}
