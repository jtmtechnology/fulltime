namespace FullTime.Api.Models;

public class User
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Email { get; set; }
    public required string PasswordHash { get; set; }
    public bool EmailVerified { get; set; }
    public string? EmailVerificationToken { get; set; }
    public DateTime? EmailVerificationTokenExpiresAt { get; set; }
    public string? PasswordResetToken { get; set; }
    public DateTime? PasswordResetTokenExpiresAt { get; set; }
    public decimal Balance { get; set; }
    public DateTime CreatedAt { get; set; }

    // ISO 3166-1 alpha-2 (e.g. "GB", "US") - drives which currency symbol this user's own amounts
    // are shown with (see Localization.CurrencyCatalog). Null means "never set", which falls back to
    // £ - the symbol every user's amounts were hardcoded to before this field existed.
    public string? Country { get; set; }
}
