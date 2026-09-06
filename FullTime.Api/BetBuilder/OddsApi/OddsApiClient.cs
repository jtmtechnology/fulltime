using FullTime.Api.BetBuilder.Dtos;
using Microsoft.Extensions.Options;

namespace FullTime.Api.BetBuilder.OddsApi;

// Wraps api.the-odds-api.com/v4. The API key is a query-string param on this provider (?apiKey=),
// not a header, unlike Highlightly/API-Football.
public class OddsApiClient(HttpClient httpClient, IOptions<OddsApiOptions> options)
{
    private string ApiKey => options.Value.ApiKey;

    // Free — listing events doesn't cost quota, only GetEventOddsAsync does.
    public async Task<List<OddsApiEventDto>> GetEventsAsync(string sportKey, CancellationToken ct = default)
    {
        using var response = await httpClient.GetAsync($"v4/sports/{sportKey}/events?apiKey={ApiKey}", ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<OddsApiEventDto>>(cancellationToken: ct) ?? [];
    }

    // Costs 1 request per (market x region) requested.
    public async Task<OddsApiEventOddsDto?> GetEventOddsAsync(
        string sportKey, string eventId, string markets, string regions = "uk", CancellationToken ct = default)
    {
        using var response = await httpClient.GetAsync(
            $"v4/sports/{sportKey}/events/{eventId}/odds?apiKey={ApiKey}&regions={regions}&markets={markets}&oddsFormat=decimal",
            ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<OddsApiEventOddsDto>(cancellationToken: ct);
    }
}
