namespace FullTime.App.Shared.Services;

// Turns "couldn't reach the server" (no connection, DNS failure, refused, dropped mid-response,
// timed out) into the ApiException every page already catches. Before this, those surfaced as
// HttpRequestException/TaskCanceledException, which most pages don't catch - on iOS an unhandled
// one in a component's OnInitializedAsync took the whole app down (an old build still pointed at
// the retired GCP IP crashed straight after the startup ad, 2026-09-28).
public class ApiNetworkErrorHandler : DelegatingHandler
{
    // Shorter than HttpClient's own 100s default so this fires first and becomes a friendly
    // ApiException, rather than HttpClient's timeout surfacing as a bare TaskCanceledException.
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    public const string UnreachableMessage = "Can't reach FullTime right now - check your connection and try again.";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);

        try
        {
            var response = await base.SendAsync(request, timeout.Token);

            // Buffered here, inside the try, so a connection dropping while the body is still
            // arriving is caught too - otherwise HttpClient buffers it after this handler returns
            // and that failure would escape as a raw HttpRequestException.
            await response.Content.LoadIntoBufferAsync(timeout.Token);
            return response;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ApiException(UnreachableMessage);
        }
        catch (HttpRequestException)
        {
            throw new ApiException(UnreachableMessage);
        }
        catch (IOException)
        {
            throw new ApiException(UnreachableMessage);
        }
    }
}
