using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace Sumodh.Crawler.Core.Fetching;

public sealed record FetchResult(bool Success, string? Html, string? FinalUrl, string? Error)
{
    public static FetchResult Fail(string error) => new(false, null, null, error);
    public static FetchResult Ok(string html, string finalUrl) => new(true, html, finalUrl, null);
}

/// <summary>Polite HTML fetcher: retries transient failures with backoff, trips a per-host
/// circuit breaker, and refuses non-HTML or oversized responses before downloading them.</summary>
public sealed class HttpFetcher
{
    public const int MaxBodyBytes = 5 * 1024 * 1024;

    private readonly IHttpClientFactory _httpFactory;
    private readonly HostCircuitBreaker _breaker;
    private readonly ILogger<HttpFetcher> _logger;
    private readonly ResiliencePipeline<HttpResponseMessage> _retry;

    public HttpFetcher(IHttpClientFactory httpFactory, HostCircuitBreaker breaker, ILogger<HttpFetcher> logger)
    {
        _httpFactory = httpFactory;
        _breaker = breaker;
        _logger = logger;
        _retry = new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = 3,
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromMilliseconds(500),
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>(ex => ex.InnerException is TimeoutException)
                    .HandleResult(r => (int)r.StatusCode >= 500 || r.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            })
            .Build();
    }

    public async Task<FetchResult> FetchAsync(string url, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return FetchResult.Fail("invalid url");

        if (_breaker.IsOpen(uri.Host))
            return FetchResult.Fail($"circuit open for {uri.Host}");

        try
        {
            var client = _httpFactory.CreateClient("crawler");
            using var response = await _retry.ExecuteAsync(
                async token => await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token), ct);

            if (!response.IsSuccessStatusCode)
            {
                _breaker.RecordFailure(uri.Host);
                return FetchResult.Fail($"http {(int)response.StatusCode}");
            }

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (!contentType.Contains("html", StringComparison.OrdinalIgnoreCase))
                return FetchResult.Fail($"skipped content-type {contentType}");

            if (response.Content.Headers.ContentLength > MaxBodyBytes)
                return FetchResult.Fail("response too large");

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var limited = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(buffer, ct)) > 0)
            {
                if (limited.Length + read > MaxBodyBytes)
                    return FetchResult.Fail("response too large");
                limited.Write(buffer, 0, read);
            }

            var html = System.Text.Encoding.UTF8.GetString(limited.ToArray());
            _breaker.RecordSuccess(uri.Host);
            var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? url;
            return FetchResult.Ok(html, finalUrl);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _breaker.RecordFailure(uri.Host);
            _logger.LogDebug("fetch failed {Url}: {Error}", url, ex.Message);
            return FetchResult.Fail(ex.Message);
        }
    }
}
