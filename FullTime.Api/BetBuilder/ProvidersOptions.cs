namespace FullTime.Api.BetBuilder;

// Rollback-by-config toggle: flip one of these and restart the systemd service to revert a bad
// cutover, no code rollback or redeploy needed. Both provider code paths (Highlightly and its
// API-Football/the-odds-api replacements) stay compiled in and DI-registered regardless of which
// is active — see Program.cs's conditional hosted-service registration.
public class ProvidersOptions
{
    public const string SectionName = "Providers";

    public string LiveScoreSource { get; set; } = "Highlightly";
    public string MarketsSource { get; set; } = "Highlightly";
}
