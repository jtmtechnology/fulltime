namespace FullTime.App.Services;

// FullTime.Api runs on a public always-on Google Cloud VM now, so every platform/device hits the
// same address — no more routing around "the emulator can't reach the host machine's localhost".
public static class ApiConfig
{
    public static string BaseUrl => "http://34.23.16.148:5199";
}
