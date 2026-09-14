namespace FullTime.App.Shared.Services;

// Per-host: only meaningful on iOS (the OS app-icon badge count), and there's nothing to clear on
// Web at all - see CLAUDE.md's per-host service pattern.
public interface IBadgeService
{
    Task ClearAsync();
}
