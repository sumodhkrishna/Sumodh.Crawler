using Sumodh.Crawler.Core.Models;
using Sumodh.Crawler.Core.Scoring;
using Sumodh.Crawler.Core.Storage;
using Sumodh.Crawler.Core.Urls;
using Microsoft.Extensions.Logging;

namespace Sumodh.Crawler.Core.Seeds;

public enum SeedSource
{
    Custom,
    Index,
    Hub
}

public sealed record Seed(string Url, string CanonicalUrl, double Score, SeedSource Source);

/// <summary>Resolves starting URLs for a job without any external search service:
/// caller-supplied URLs first (highest priority), then warm seeds from our own persistent
/// index, with bundled hub pages always added so the public web stays in scope.</summary>
public sealed class SeedResolver(PageStore pages, HubSeedSource hubs, ILogger<SeedResolver> logger)
{
    private const int MaxIndexSeeds = 20;
    private const double CustomSeedScore = 0.95;
    private const double IndexSeedScore = 0.9;
    private const double IndexLinkSeedScore = 0.5;
    private const double HubSeedScore = 0.3;

    public async Task<List<Seed>> ResolveAsync(string topic, RelevanceScorer scorer,
        IReadOnlyList<string>? customUrls = null, CancellationToken ct = default)
    {
        var seeds = new List<Seed>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // 1. Caller-supplied URLs: crawled first, but they supplement public-web
        //    discovery below rather than replacing it.
        foreach (var url in customUrls ?? [])
        {
            var canonical = UrlCanonicalizer.Canonicalize(url);
            if (canonical is null)
            {
                logger.LogWarning("ignoring invalid seed url: {Url}", url);
                continue;
            }
            if (seen.Add(canonical))
                seeds.Add(new Seed(url, canonical, CustomSeedScore, SeedSource.Custom));
        }

        // 2. Warm seeds: pages from earlier crawls that match the topic, plus their
        //    topical outbound links — both re-crawled at high priority.
        try
        {
            var hits = await pages.SearchAsync(topic, MaxIndexSeeds, ct);
            foreach (var hit in hits)
            {
                if (seen.Add(hit.CanonicalUrl))
                    seeds.Add(new Seed(hit.Url, hit.CanonicalUrl, IndexSeedScore, SeedSource.Index));

                foreach (var link in hit.OutboundLinks)
                {
                    var linkScore = scorer.ScoreLink(link.Anchor, "", IndexSeedScore, depth: 1);
                    if (linkScore < IndexLinkSeedScore) continue;
                    var canonical = UrlCanonicalizer.Canonicalize(link.Url);
                    if (canonical is not null && seen.Add(canonical))
                        seeds.Add(new Seed(link.Url, canonical, linkScore, SeedSource.Index));
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning("index seed lookup failed (fresh database?): {Error}", ex.Message);
        }

        // 3. Cold-start floor: bundled topic-agnostic hubs; the scorer steers from there.
        //    Always added — the crawl never narrows to just the supplied URLs.
        foreach (var url in hubs.GetHubs())
        {
            var canonical = UrlCanonicalizer.Canonicalize(url);
            if (canonical is not null && seen.Add(canonical))
                seeds.Add(new Seed(url, canonical, HubSeedScore, SeedSource.Hub));
        }

        logger.LogInformation("resolved {Total} seeds ({Custom} custom, {Warm} from index, {Hubs} hubs) for topic '{Topic}'",
            seeds.Count, seeds.Count(s => s.Source == SeedSource.Custom),
            seeds.Count(s => s.Source == SeedSource.Index),
            seeds.Count(s => s.Source == SeedSource.Hub), topic);
        return seeds;
    }
}
