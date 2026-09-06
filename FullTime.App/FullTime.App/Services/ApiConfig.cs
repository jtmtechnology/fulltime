namespace FullTime.App.Services;

// FullTime.Api runs on a public always-on Google Cloud VM now, so every platform/device hits the
// same address — no more routing around "the emulator can't reach the host machine's localhost".
public static class ApiConfig
{
    // TEMPORARY: pointed at FullTime.Api.Sandbox (port 5299), not the real production API (5199),
    // to test API-Football/the-odds-api player-prop markets end-to-end in the emulator - see
    // HANDOVER.md. Switch back to :5199 before this ships anywhere real; the sandbox has no auth,
    // no bet placement, no leagues - only /api/matches/upcoming and /api/matches/{id}/bet-builder-
    // markets are implemented, matching one real match (Everton vs Manchester United, 2026-09-06).
    public static string BaseUrl => "http://34.23.16.148:5299";
}
