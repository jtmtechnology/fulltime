using System.Net.Http.Headers;
using FullTime.Api.Sandbox.Dtos;

namespace FullTime.Api.Sandbox.Services;

// Wraps the direct api-sports.io dashboard API (not RapidAPI - see ApiFootballOptions). Every
// method returns the response's own rate-limit headers alongside the data so the test controller
// can surface real quota usage back to the caller - see RateLimitInfo.
public class ApiFootballClient(HttpClient httpClient)
{
    private static readonly string[] RateLimitHeaderNames =
    [
        "x-ratelimit-limit", "x-ratelimit-remaining",
        "x-ratelimit-requests-limit", "x-ratelimit-requests-remaining",
    ];

    // /fixtures?live=all - confirmed 2026-09-06 this returns every live match worldwide in a
    // single request, unlike Highlightly's one-call-per-league model. Filter down to tracked
    // leagues client-side after the call, not via a query param (there isn't one for "live in
    // these leagues only").
    public Task<ApiCallResult<List<FixtureDto>>> GetLiveFixturesAsync(CancellationToken ct = default) =>
        GetFixturesAsync("fixtures?live=all", ct);

    // Fixture discovery for one league/season/date-range - the per-league call this provider still
    // needs (there's no "all upcoming fixtures across leagues" endpoint), same shape as Highlightly's
    // RefreshFixturesAsync. Cheap since it only needs to run ~once/day per league.
    public Task<ApiCallResult<List<FixtureDto>>> GetFixturesAsync(
        int leagueId, int season, DateOnly from, DateOnly to, CancellationToken ct = default) =>
        GetFixturesAsync(
            $"fixtures?league={leagueId}&season={season}&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}", ct);

    // /players/squads?team={id} - one call per team, used only to resolve which team a
    // the-odds-api player-prop outcome belongs to (see TestController.PlayerProps). Not cached
    // across calls - this is a one-shot evaluation project, not something meant to run continuously.
    public async Task<ApiCallResult<List<string>>> GetSquadPlayerNamesAsync(long teamId, CancellationToken ct = default)
    {
        using var response = await httpClient.GetAsync($"players/squads?team={teamId}", ct);
        var rateLimit = ExtractRateLimitInfo(response.Headers);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ApiFootballResponse<SquadDto>>(cancellationToken: ct)
            ?? new ApiFootballResponse<SquadDto>();
        // API-Football HTML-encodes some squad names (confirmed: Everton's "J. O'Brien" comes back
        // as "J. O&apos;Brien") - the-odds-api doesn't, so this needs decoding before any surname
        // comparison against it.
        var names = body.Response.FirstOrDefault()?.Players
            .Select(p => System.Net.WebUtility.HtmlDecode(p.Name)).ToList() ?? [];
        return new ApiCallResult<List<string>>(names, rateLimit);
    }

    private async Task<ApiCallResult<List<FixtureDto>>> GetFixturesAsync(string requestUri, CancellationToken ct)
    {
        using var response = await httpClient.GetAsync(requestUri, ct);
        var rateLimit = ExtractRateLimitInfo(response.Headers);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ApiFootballResponse<FixtureDto>>(cancellationToken: ct)
            ?? new ApiFootballResponse<FixtureDto>();
        return new ApiCallResult<List<FixtureDto>>(body.Response, rateLimit);
    }

    private static RateLimitInfo ExtractRateLimitInfo(HttpResponseHeaders headers)
    {
        var found = new Dictionary<string, string>();
        foreach (var name in RateLimitHeaderNames)
        {
            if (headers.TryGetValues(name, out var values))
            {
                found[name] = string.Join(",", values);
            }
        }

        return new RateLimitInfo(found);
    }
}
