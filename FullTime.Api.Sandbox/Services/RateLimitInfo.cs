namespace FullTime.Api.Sandbox.Services;

// Surfaced back through every test endpoint so quota usage is visible while poking at this from
// curl, without needing a separate VM log-tail - the whole point of this project is watching cost
// behavior while evaluating these providers, not just correctness.
public record RateLimitInfo(Dictionary<string, string> Headers);

public record ApiCallResult<T>(T Data, RateLimitInfo RateLimit);
