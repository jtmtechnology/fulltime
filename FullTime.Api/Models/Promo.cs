namespace FullTime.Api.Models;

// Owner-authored announcements for the Matches page carousel, alongside the Free Spin and Bet
// Builder Boost slides. There's no admin UI - rows are inserted by hand (see HANDOVER.md), so the
// API filters on the dates/IsActive itself rather than trusting whoever wrote the row to tidy up.
public class Promo
{
    public Guid Id { get; set; }
    public required string Title { get; set; }
    public string? Subtitle { get; set; }

    // An emoji, or an https image URL.
    public string? Icon { get; set; }

    // One of the fixed styles the app knows ("accent", "gold", "blue"); anything else falls back to
    // "accent" so a typo can't produce an unstyled banner.
    public string Theme { get; set; } = "accent";

    // In-app route only (must start with "/"). External links are deliberately unsupported - see
    // PromosController.
    public string? Link { get; set; }

    public DateTime? StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }

    // Higher shows first among custom promos. Free Spin and Boost always come before any of these.
    public int Priority { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}
