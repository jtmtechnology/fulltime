namespace FullTime.App.Services;

// FullTime.Api runs on a public always-on Google Cloud VM now, so every platform/device hits the
// same address — no more routing around "the emulator can't reach the host machine's localhost".
//
// TEMPORARY for local Bet Builder testing: Debug builds hit the local dev API instead, via the
// Android emulator's fixed host-loopback alias (10.0.2.2). Revert to the VM-only version once
// local testing is done — don't ship a debug-pointed BaseUrl.
public static class ApiConfig
{
#if DEBUG
    public static string BaseUrl => "http://10.0.2.2:5299";
#else
    public static string BaseUrl => "http://34.23.16.148:5199";
#endif
}
