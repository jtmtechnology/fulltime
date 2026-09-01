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

    // Daily Spinner state - local date (not UTC) of this user's last spin, and their current streak
    // as of that spin. A new day (relative to LastSpinDate) is what re-opens the daily gate.
    public DateOnly? LastSpinDate { get; set; }
    public int SpinStreak { get; set; }

    // Captured from the MAUI app (only host that does push) each time it registers for push
    // notifications - see DevicesController.Register. Null until the app's been opened at least
    // once; SpinReminderService skips anyone without it rather than guessing UTC. Whole minutes,
    // not a full IANA zone - good enough for a once-a-day reminder, not DST-transition-precise.
    public int? UtcOffsetMinutes { get; set; }

    // The local date (per UtcOffsetMinutes) SpinReminderService last sent this user a "you haven't
    // spun today" push - keeps the reminder to once per local day, independent of LastSpinDate.
    public DateOnly? LastSpinReminderDate { get; set; }

    // Set by a "Bet Boost"/"2x Odds Boost" spin prize, consumed (and cleared) by the very next bet
    // placed in BetService - always the single most recently won boost, never stacked; landing a
    // new boost while one is already pending overwrites it rather than queuing.
    public decimal? PendingBoostMultiplier { get; set; }
    public string? PendingBoostLabel { get; set; }

    // ISO 3166-1 alpha-2 (e.g. "GB", "US") - drives which currency symbol this user's own amounts
    // are shown with (see Localization.CurrencyCatalog). Null means "never set", which falls back to
    // £ - the symbol every user's amounts were hardcoded to before this field existed.
    public string? Country { get; set; }
}
