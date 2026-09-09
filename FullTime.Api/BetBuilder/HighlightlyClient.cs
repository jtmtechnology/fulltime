using System.Net;
using FullTime.Api.Auth;
using FullTime.Api.BetBuilder.Dtos;
using Microsoft.Extensions.Options;

namespace FullTime.Api.BetBuilder;

// Paths here are bare ("matches", "odds", "events/{id}"), not prefixed "football/" - confirmed real
// 2026-09-06: RapidAPI's proxy namespaces multiple sports under one host
// (sport-highlights-api.p.rapidapi.com/football/matches), but this project moved to a direct
// Highlightly account on a sport-specific host (soccer.highlightly.net, see HighlightlyOptions)
// whose paths are already scoped to football and don't repeat it - the "football/" prefix 404s
// there. If HighlightlyOptions.ApiHost ever points back at the RapidAPI proxy, these paths would
// need the prefix restored.
public class HighlightlyClient(
    HttpClient httpClient, IOptions<HighlightlyOptions> options, IEmailSender emailSender, ILogger<HighlightlyClient> logger)
{
    private static readonly TimeSpan MinRequestInterval = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan QuotaCooldown = TimeSpan.FromMinutes(15);
    private readonly SemaphoreSlim _throttleGate = new(1, 1);
    private DateTime _lastRequestUtc = DateTime.MinValue;

    // Static and shared across every HighlightlyClient instance - AddHttpClient<HighlightlyClient>
    // makes this a transient typed client, so each background loop (live-score sync, fixture
    // discovery, odds sync, goal-scorer resolution) injects its own instance. A 429 here is
    // RapidAPI's daily/monthly quota cap, not a per-second burst limit (requests are already
    // serialized to one every 300ms below), so it won't clear in seconds. Without this shared gate,
    // every independent loop rediscovers the same exhaustion on its own schedule and retries it away
    // with its own attempts, multiplying the wasted calls instead of sharing the "account is cooling
    // down" fact - this is what pushed usage from 85% to 100% within minutes.
    private static DateTime _quotaExhaustedUntilUtc = DateTime.MinValue;

    // Self-tracked call counter for quota alerting - confirmed 2026-09-09 that Highlightly's own
    // 429 response carries no remaining-quota/reset-time header at all (just a plain
    // {"message":"You have breached your daily request limits."} body), so there's no live endpoint
    // to poll for "how much is left". Counting our own real outbound calls (below, right before each
    // actual HTTP request - calls skipped by the cooldown gate above don't count, since they never
    // reach the provider) is the closest available proxy. Resets on the first call of a new UTC date;
    // also resets on every process restart (in-memory only, same as _quotaExhaustedUntilUtc above),
    // so a redeploy loses same-day progress toward the threshold - acceptable imprecision for an
    // early-warning signal, not a billing-accurate meter.
    private static int _dailyCallCount;
    private static DateOnly _countDate = DateOnly.MinValue;
    private static DateOnly? _thresholdAlertSentDate;
    private static DateOnly? _exhaustionAlertSentDate;

    public async Task<List<MatchDto>> GetMatchesAsync(int leagueId, int season, DateOnly date, CancellationToken ct = default)
    {
        var result = await GetWithRetryAsync<MatchesResponse>(
            $"matches?leagueId={leagueId}&season={season}&date={date:yyyy-MM-dd}&limit=100", ct);
        return result?.Data ?? [];
    }

    // Paginated at 5 matches per page by the provider (each match's own "odds" array carries every
    // bookmaker/market combination for it, so this stays 5 matches/page regardless of whether
    // bookmakerName filters it down) — callers loop, bumping offset by the page size until it
    // reaches the response's own totalCount. bookmakerName is optional: omitting it returns every
    // bookmaker's price for every market, which BetBuilderSyncService relies on for the 1X2 price
    // (deliberately not pinned to one bookmaker, unlike the Bet Builder extra markets).
    public async Task<OddsResponse?> GetOddsAsync(
        int leagueId, DateOnly date, int offset, string? bookmakerName = null, CancellationToken ct = default)
    {
        var bookmakerParam = bookmakerName is null ? "" : $"&bookmakerName={Uri.EscapeDataString(bookmakerName)}";
        return await GetWithRetryAsync<OddsResponse>(
            $"odds?leagueId={leagueId}&date={date:yyyy-MM-dd}{bookmakerParam}" +
            $"&oddsType=prematch&limit=5&offset={offset}", ct);
    }

    public async Task<List<MatchEventDto>> GetEventsAsync(long matchId, CancellationToken ct = default)
    {
        var result = await GetWithRetryAsync<List<MatchEventDto>>($"events/{matchId}", ct);
        return result ?? [];
    }

    // Team-level (not per-player) match statistics - only used for the "Corners" entry, see
    // BetBuilderSyncService.ResolveMatchEventsAsync.
    public async Task<List<TeamStatisticsDto>> GetStatisticsAsync(long matchId, CancellationToken ct = default)
    {
        var result = await GetWithRetryAsync<List<TeamStatisticsDto>>($"statistics/{matchId}", ct);
        return result ?? [];
    }

    private async Task<T?> GetWithRetryAsync<T>(string requestUri, CancellationToken ct)
    {
        var cooldownRemaining = _quotaExhaustedUntilUtc - DateTime.UtcNow;
        if (cooldownRemaining > TimeSpan.Zero)
        {
            throw new HttpRequestException(
                $"Highlightly quota cooling down for another {cooldownRemaining.TotalSeconds:F0}s, skipping {requestUri}");
        }

        await WaitForThrottleSlotAsync(ct);
        RecordCallForQuotaTracking();
        using var response = await httpClient.GetAsync(requestUri, ct);
        var isThrottled = response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.Forbidden;
        if (!isThrottled)
        {
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
        }

        var cooldown = response.Headers.RetryAfter?.Delta is { } retryAfter && retryAfter > QuotaCooldown
            ? retryAfter
            : QuotaCooldown;
        _quotaExhaustedUntilUtc = DateTime.UtcNow.Add(cooldown);
        logger.LogWarning(
            "Throttled ({StatusCode}) on {RequestUri} - treating as quota exhaustion, cooling down all Highlightly calls until {Until:O}",
            response.StatusCode, requestUri, _quotaExhaustedUntilUtc);
        MaybeAlertExhausted();
        response.EnsureSuccessStatusCode();
        return default;
    }

    // Called right before every real outbound request (not calls skipped by the cooldown gate
    // above, which never reach the provider). Fires the proactive "approaching the limit" email
    // exactly once per UTC date, the first time the running count crosses the configured threshold -
    // see HighlightlyOptions.DailyCallBudget/AlertThresholdPercent.
    private void RecordCallForQuotaTracking()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (today != _countDate)
        {
            _countDate = today;
            _dailyCallCount = 0;
        }

        var count = Interlocked.Increment(ref _dailyCallCount);
        var opts = options.Value;
        var threshold = opts.DailyCallBudget * opts.AlertThresholdPercent / 100;

        if (count < threshold || _thresholdAlertSentDate == today)
        {
            return;
        }

        _thresholdAlertSentDate = today;
        SendAlertEmail(
            "FullTime: Highlightly quota approaching daily limit",
            $"Highlightly has made {count} calls today (UTC {today:yyyy-MM-dd}), crossing the " +
            $"{opts.AlertThresholdPercent}% warning threshold of its {opts.DailyCallBudget}/day budget. " +
            "No outage yet, but live scores/odds may stop updating if the account actually hits its cap.");
    }

    // Fires the moment we get an actual 429/403 from the provider - a much stronger signal than the
    // proactive count-based warning above (that one estimates; this one is ground truth), and the two
    // are complementary: the threshold email gives advance notice, this one confirms it actually
    // happened. Also fires exactly once per UTC date.
    private void MaybeAlertExhausted()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (_exhaustionAlertSentDate == today)
        {
            return;
        }

        _exhaustionAlertSentDate = today;
        SendAlertEmail(
            "FullTime: Highlightly quota exhausted",
            $"Highlightly just returned a 429/403 (UTC {DateTime.UtcNow:yyyy-MM-dd HH:mm}) and calls are " +
            $"now cooling down until {_quotaExhaustedUntilUtc:yyyy-MM-dd HH:mm} UTC. Live scores and odds " +
            "have stopped updating for every tracked league until this clears.");
    }

    // Fire-and-forget by design: a failed alert email must never break the sync loop that
    // triggered it. IEmailSender is a singleton (see Program.cs), so it's safe to use from this
    // background context with no request-scoped dependencies involved.
    private void SendAlertEmail(string subject, string body)
    {
        var toEmail = options.Value.AlertEmail;
        if (string.IsNullOrWhiteSpace(toEmail))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await emailSender.SendAsync(toEmail, subject, body);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to send Highlightly quota alert email to {ToEmail}", toEmail);
            }
        });
    }

    private async Task WaitForThrottleSlotAsync(CancellationToken ct)
    {
        await _throttleGate.WaitAsync(ct);
        try
        {
            var wait = MinRequestInterval - (DateTime.UtcNow - _lastRequestUtc);
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, ct);
            }

            _lastRequestUtc = DateTime.UtcNow;
        }
        finally
        {
            _throttleGate.Release();
        }
    }
}
