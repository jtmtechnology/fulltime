using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FullTime.App.Shared.Services;

public record JwtClaims(
    [property: JsonPropertyName("sub")] string Sub,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("email")] string Email);

// Scoped (not Singleton) in both hosts — Blazor Server needs one instance per user circuit, and
// Scoped works fine for MAUI's single-user app too.
public class AuthState(IJwtStore jwtStore)
{
    public string? Token { get; private set; }
    public JwtClaims? Claims { get; private set; }
    public bool IsLoggedIn => Token is not null;

    public event Action? Changed;

    public async Task InitializeAsync()
    {
        Token = await jwtStore.GetAsync();
        Claims = Decode(Token);
        Changed?.Invoke();
    }

    public async Task SetTokenAsync(string token)
    {
        Token = token;
        Claims = Decode(token);
        await jwtStore.SetAsync(token);
        Changed?.Invoke();
    }

    public async Task LogoutAsync()
    {
        Token = null;
        Claims = null;
        await jwtStore.ClearAsync();
        Changed?.Invoke();
    }

    private static JwtClaims? Decode(string? token)
    {
        if (token is null) return null;

        try
        {
            var payload = token.Split('.')[1];
            var padded = payload.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            return JsonSerializer.Deserialize<JwtClaims>(json);
        }
        catch
        {
            return null;
        }
    }
}
