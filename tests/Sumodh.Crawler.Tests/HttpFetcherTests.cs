using System.Net;
using Sumodh.Crawler.Core.Fetching;
using Microsoft.Extensions.Logging.Abstractions;

namespace Sumodh.Crawler.Tests;

public class HttpFetcherTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(responder(request));
        }
    }

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static HttpResponseMessage Html(string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "text/html"),
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://example.com/")
        };
        return response;
    }

    private static HttpFetcher Fetcher(HttpMessageHandler handler, HostCircuitBreaker? breaker = null) =>
        new(new StubFactory(handler), breaker ?? new HostCircuitBreaker(), NullLogger<HttpFetcher>.Instance);

    [Fact]
    public async Task Returns_html_on_success()
    {
        var handler = new StubHandler(_ => Html("<html>hello</html>"));
        var result = await Fetcher(handler).FetchAsync("https://example.com/");

        Assert.True(result.Success);
        Assert.Contains("hello", result.Html);
    }

    [Fact]
    public async Task Retries_transient_500_then_succeeds()
    {
        var attempts = 0;
        var handler = new StubHandler(_ =>
            ++attempts < 3 ? Html("err", HttpStatusCode.InternalServerError) : Html("<html>ok</html>"));

        var result = await Fetcher(handler).FetchAsync("https://example.com/");

        Assert.True(result.Success);
        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task Does_not_retry_404()
    {
        var handler = new StubHandler(_ => Html("missing", HttpStatusCode.NotFound));
        var result = await Fetcher(handler).FetchAsync("https://example.com/gone");

        Assert.False(result.Success);
        Assert.Equal(1, handler.Calls);
        Assert.Equal("http 404", result.Error);
    }

    [Fact]
    public async Task Skips_non_html_content()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([1, 2, 3])
            {
                Headers = { ContentType = new("application/pdf") }
            }
        });
        var result = await Fetcher(handler).FetchAsync("https://example.com/doc.pdf");

        Assert.False(result.Success);
        Assert.Contains("content-type", result.Error);
    }

    [Fact]
    public async Task Circuit_opens_after_repeated_failures()
    {
        var breaker = new HostCircuitBreaker(failureThreshold: 2, cooldownSeconds: 60);
        var handler = new StubHandler(_ => Html("err", HttpStatusCode.NotFound));
        var fetcher = Fetcher(handler, breaker);

        await fetcher.FetchAsync("https://dead.example.com/a");
        await fetcher.FetchAsync("https://dead.example.com/b");
        var result = await fetcher.FetchAsync("https://dead.example.com/c");

        Assert.False(result.Success);
        Assert.Contains("circuit open", result.Error);
        Assert.Equal(2, handler.Calls); // third request never hit the network
    }

    [Fact]
    public async Task Circuit_failures_reset_on_success()
    {
        var breaker = new HostCircuitBreaker(failureThreshold: 2, cooldownSeconds: 60);
        var fail = false;
        var handler = new StubHandler(_ =>
            (fail = !fail) ? Html("err", HttpStatusCode.NotFound) : Html("<html>ok</html>"));
        var fetcher = Fetcher(handler, breaker);

        for (var i = 0; i < 4; i++)
            await fetcher.FetchAsync("https://flaky.example.com/");

        // Alternating fail/success never trips a threshold of 2 consecutive failures.
        Assert.False(breaker.IsOpen("flaky.example.com"));
    }

    [Fact]
    public async Task Rejects_invalid_url()
    {
        var handler = new StubHandler(_ => Html("ok"));
        var result = await Fetcher(handler).FetchAsync("not-a-url");
        Assert.False(result.Success);
    }
}
