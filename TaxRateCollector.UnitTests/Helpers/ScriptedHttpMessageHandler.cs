namespace TaxRateCollector.UnitTests.Helpers;

/// <summary>
/// HttpMessageHandler driven by a caller-supplied router, for tests that need to
/// distinguish requests that share a URL (e.g. ScraperHttpHelper's direct / browser-UA /
/// browser-heavy tiers all hit the same target URL and differ only by headers).
/// Records a tag per request so tests can assert call order as well as responses.
/// </summary>
public sealed class ScriptedHttpMessageHandler(
    Func<HttpRequestMessage, (string Tag, HttpResponseMessage Response)> router)
    : HttpMessageHandler
{
    public List<string> CallLog { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var (tag, response) = router(request);
        CallLog.Add(tag);
        response.RequestMessage ??= request;
        return Task.FromResult(response);
    }
}
