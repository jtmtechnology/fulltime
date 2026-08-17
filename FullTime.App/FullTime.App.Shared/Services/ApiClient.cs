using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FullTime.App.Shared.Models;

namespace FullTime.App.Shared.Services;

public record LoginOutcome(bool Success, bool EmailNotVerified, LoginResponse? Response, string? Error);

// Scoped, same as AuthState — one HttpClient/token pairing per user circuit (Web) or per app
// session (MAUI). BaseAddress is configured per-host at DI registration time.
public class ApiClient(HttpClient httpClient, AuthState authState)
{
    private void Authorize()
    {
        httpClient.DefaultRequestHeaders.Authorization = authState.Token is null
            ? null
            : new AuthenticationHeaderValue("Bearer", authState.Token);
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage res)
    {
        try
        {
            var body = await res.Content.ReadFromJsonAsync<MessageResponse>();
            return body?.Error ?? $"Request failed: {(int)res.StatusCode}";
        }
        catch
        {
            return $"Request failed: {(int)res.StatusCode}";
        }
    }

    public async Task<Guid> RegisterAsync(string name, string email, string password)
    {
        var res = await httpClient.PostAsJsonAsync("api/auth/register", new RegisterRequest(name, email, password));
        if (!res.IsSuccessStatusCode) throw new ApiException(await ReadErrorAsync(res), res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<RegisterResponse>();
        return body!.UserId;
    }

    public async Task<LoginOutcome> LoginAsync(string email, string password)
    {
        var res = await httpClient.PostAsJsonAsync("api/auth/login", new LoginRequest(email, password));

        if (res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadFromJsonAsync<LoginResponse>();
            return new LoginOutcome(true, false, body, null);
        }

        if (res.StatusCode == HttpStatusCode.Forbidden)
        {
            return new LoginOutcome(false, true, null, null);
        }

        return new LoginOutcome(false, false, null, await ReadErrorAsync(res));
    }

    public async Task<string> VerifyEmailAsync(string token)
    {
        var res = await httpClient.PostAsJsonAsync("api/auth/verify-email", new VerifyEmailRequest(token));
        if (!res.IsSuccessStatusCode) throw new ApiException(await ReadErrorAsync(res), res.StatusCode);
        return "Email verified! You can log in now.";
    }

    public async Task<string> ResendVerificationAsync(string email)
    {
        var res = await httpClient.PostAsJsonAsync("api/auth/resend-verification", new ResendVerificationRequest(email));
        var body = await res.Content.ReadFromJsonAsync<MessageResponse>();
        return body?.Message ?? "If that account exists and isn't verified yet, a new verification email has been sent.";
    }

    public async Task<string> ForgotPasswordAsync(string email)
    {
        var res = await httpClient.PostAsJsonAsync("api/auth/forgot-password", new ForgotPasswordRequest(email));
        var body = await res.Content.ReadFromJsonAsync<MessageResponse>();
        return body?.Message ?? "If that email is registered, a password reset link has been sent.";
    }

    public async Task<string> ResetPasswordAsync(string token, string newPassword)
    {
        var res = await httpClient.PostAsJsonAsync("api/auth/reset-password", new ResetPasswordRequest(token, newPassword));
        if (!res.IsSuccessStatusCode) throw new ApiException(await ReadErrorAsync(res), res.StatusCode);
        return "Password reset — you can log in with your new password now.";
    }

    public async Task<MeDto> GetMeAsync()
    {
        Authorize();
        var res = await httpClient.GetAsync("api/users/me");
        if (!res.IsSuccessStatusCode) throw new ApiException(await ReadErrorAsync(res), res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<MeDto>())!;
    }

    public async Task<MeDto> UpdateProfileAsync(string name)
    {
        Authorize();
        var res = await httpClient.PutAsJsonAsync("api/users/me", new UpdateProfileRequest(name));
        if (!res.IsSuccessStatusCode) throw new ApiException(await ReadErrorAsync(res), res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<MeDto>())!;
    }

    public async Task ChangePasswordAsync(string currentPassword, string newPassword)
    {
        Authorize();
        var res = await httpClient.PostAsJsonAsync("api/users/me/change-password", new ChangePasswordRequest(currentPassword, newPassword));
        if (!res.IsSuccessStatusCode) throw new ApiException(await ReadErrorAsync(res), res.StatusCode);
    }

    public async Task<List<UpcomingMatchDto>> GetUpcomingMatchesAsync(DateOnly? date = null)
    {
        var url = date is { } d ? $"api/matches/upcoming?date={d:yyyy-MM-dd}" : "api/matches/upcoming";
        return await httpClient.GetFromJsonAsync<List<UpcomingMatchDto>>(url) ?? [];
    }

    public async Task<ConfigResponse> GetConfigAsync() =>
        await httpClient.GetFromJsonAsync<ConfigResponse>("api/config") ?? new ConfigResponse(600);

    public async Task<BetDto> PlaceBetAsync(decimal stake, List<SelectionRequest> selections, Guid? leagueId)
    {
        Authorize();
        var res = await httpClient.PostAsJsonAsync("api/bets", new PlaceBetRequest(stake, selections, leagueId));
        if (!res.IsSuccessStatusCode) throw new ApiException(await ReadErrorAsync(res), res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<BetDto>())!;
    }

    public async Task<List<BetDto>> GetMyBetsAsync()
    {
        Authorize();
        var res = await httpClient.GetAsync("api/bets/me");
        if (!res.IsSuccessStatusCode) throw new ApiException(await ReadErrorAsync(res), res.StatusCode);
        return await res.Content.ReadFromJsonAsync<List<BetDto>>() ?? [];
    }

    public async Task<List<LeaderboardEntryDto>> GetLeaderboardAsync()
    {
        Authorize();
        var res = await httpClient.GetAsync("api/leaderboard");
        if (!res.IsSuccessStatusCode) throw new ApiException(await ReadErrorAsync(res), res.StatusCode);
        return await res.Content.ReadFromJsonAsync<List<LeaderboardEntryDto>>() ?? [];
    }

    public async Task<List<LeagueSummaryDto>> GetMyLeaguesAsync()
    {
        Authorize();
        var res = await httpClient.GetAsync("api/leagues/mine");
        if (!res.IsSuccessStatusCode) throw new ApiException(await ReadErrorAsync(res), res.StatusCode);
        return await res.Content.ReadFromJsonAsync<List<LeagueSummaryDto>>() ?? [];
    }

    public async Task<LeagueSummaryDto> CreateLeagueAsync(string name)
    {
        Authorize();
        var res = await httpClient.PostAsJsonAsync("api/leagues", new CreateLeagueRequest(name));
        if (!res.IsSuccessStatusCode) throw new ApiException(await ReadErrorAsync(res), res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<LeagueSummaryDto>())!;
    }

    public async Task<LeagueSummaryDto> JoinLeagueAsync(string inviteCode)
    {
        Authorize();
        var res = await httpClient.PostAsJsonAsync("api/leagues/join", new JoinLeagueRequest(inviteCode));
        if (!res.IsSuccessStatusCode) throw new ApiException(await ReadErrorAsync(res), res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<LeagueSummaryDto>())!;
    }

    public async Task<List<LeaderboardEntryDto>> GetLeagueLeaderboardAsync(Guid leagueId)
    {
        Authorize();
        var res = await httpClient.GetAsync($"api/leagues/{leagueId}/leaderboard");
        if (!res.IsSuccessStatusCode) throw new ApiException(await ReadErrorAsync(res), res.StatusCode);
        return await res.Content.ReadFromJsonAsync<List<LeaderboardEntryDto>>() ?? [];
    }

    public async Task LeaveLeagueAsync(Guid leagueId)
    {
        Authorize();
        var res = await httpClient.DeleteAsync($"api/leagues/{leagueId}/membership");
        if (!res.IsSuccessStatusCode) throw new ApiException(await ReadErrorAsync(res), res.StatusCode);
    }
}
