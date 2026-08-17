namespace FullTime.Api.Auth;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public required string SigningKey { get; set; }
    public string Issuer { get; set; } = "FullTime.Api";
    public string Audience { get; set; } = "FullTime.Api";
    public int ExpiryMinutes { get; set; } = 43200;
}
