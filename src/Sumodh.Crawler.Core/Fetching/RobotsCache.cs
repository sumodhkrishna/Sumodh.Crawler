using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Sumodh.Crawler.Core.Fetching;

/// <summary>Per-host robots.txt rules, fetched once and cached in memory for 24h.</summary>
public sealed class RobotsCache(IHttpClientFactory httpFactory, ILogger<RobotsCache> logger)
{
    public const string UserAgent = "TopicCrawler";
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

    private sealed record Entry(RobotsRules Rules, DateTime FetchedAt);
    private readonly ConcurrentDictionary<string, Lazy<Task<Entry>>> _cache = new();

    public async Task<RobotsRules> GetAsync(Uri uri, CancellationToken ct = default)
    {
        var key = $"{uri.Scheme}://{uri.Host}:{uri.Port}";
        var lazy = _cache.GetOrAdd(key, _ => new Lazy<Task<Entry>>(() => FetchAsync(uri)));
        var entry = await lazy.Value.WaitAsync(ct);

        if (DateTime.UtcNow - entry.FetchedAt > Ttl)
        {
            _cache.TryRemove(key, out _);
            return await GetAsync(uri, ct);
        }
        return entry.Rules;
    }

    private async Task<Entry> FetchAsync(Uri uri)
    {
        var robotsUrl = $"{uri.Scheme}://{uri.Authority}/robots.txt";
        try
        {
            var client = httpFactory.CreateClient("robots");
            using var response = await client.GetAsync(robotsUrl);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return new Entry(RobotsParser.Parse(content, UserAgent), DateTime.UtcNow);
            }
            // 4xx (incl. 404) → no restrictions; 5xx → be conservative but don't block forever.
            return new Entry(RobotsRules.AllowAll, DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            logger.LogDebug("robots.txt fetch failed for {Host}: {Error}", uri.Host, ex.Message);
            return new Entry(RobotsRules.AllowAll, DateTime.UtcNow);
        }
    }
}
