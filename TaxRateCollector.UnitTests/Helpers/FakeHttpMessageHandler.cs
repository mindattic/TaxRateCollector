using System.Net;
using System.Net.Http.Headers;

namespace TaxRateCollector.UnitTests.Helpers;

/// <summary>
/// Minimal fake HttpMessageHandler for unit tests.
/// Maps request URLs to canned responses; unregistered URLs return 404.
/// </summary>
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Dictionary<string, (byte[] body, string mime)> _responses = new();

    public void Register(string url, string body, string mime = "text/html")
        => _responses[url] = (System.Text.Encoding.UTF8.GetBytes(body), mime);

    public void Register(string url, byte[] body, string mime)
        => _responses[url] = (body, mime);

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        var key = request.RequestUri?.ToString() ?? "";
        if (!_responses.TryGetValue(key, out var entry))
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

        var resp = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(entry.body)
        };
        resp.Content.Headers.ContentType = new MediaTypeHeaderValue(entry.mime);
        return Task.FromResult(resp);
    }
}

/// <summary>
/// Fake IHttpClientFactory that always returns a client backed by a given handler.
/// </summary>
public sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler);
}
