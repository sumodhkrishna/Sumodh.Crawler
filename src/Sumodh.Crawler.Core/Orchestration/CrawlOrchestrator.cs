using System.Security.Cryptography;
using System.Text;
using Sumodh.Crawler.Core.Extraction;
using Sumodh.Crawler.Core.Fetching;
using Sumodh.Crawler.Core.Frontier;
using Sumodh.Crawler.Core.Models;
using Sumodh.Crawler.Core.Scoring;
using Sumodh.Crawler.Core.Seeds;
using Sumodh.Crawler.Core.Storage;
using Sumodh.Crawler.Core.Urls;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Sumodh.Crawler.Core.Orchestration;

/// <summary>Runs crawl jobs: resolves seeds, fans out workers over the shared frontier,
/// and drives the claim → robots → rate-limit → fetch → extract → score → store →
/// expand pipeline until a stop condition is hit.</summary>
public sealed class CrawlOrchestrator(
    JobQueue queue,
    MongoContext mongo,
    JobStore jobs,
    PageStore pages,
    MongoFrontier frontier,
    SeedResolver seedResolver,
    RobotsCache robots,
    DomainRateLimiter rateLimiter,
    HttpFetcher fetcher,
    HtmlExtractor extractor,
    ILogger<CrawlOrchestrator> logger) : BackgroundService
{
    private const int MaxParallelJobs = 2;
    private const int MaxLinksPerPage = 60;
    private const int MinTextLength = 120;
    private static readonly TimeSpan MaxCrawlDelay = TimeSpan.FromSeconds(10);

    private sealed class JobRuntime
    {
        public long Fetched, Stored, Skipped, Failed, Reused;
        public int InFlight;
        public JobCounts Snapshot() => new()
        {
            Fetched = Volatile.Read(ref Fetched),
            Stored = Volatile.Read(ref Stored),
            Skipped = Volatile.Read(ref Skipped),
            Failed = Volatile.Read(ref Failed),
            Reused = Volatile.Read(ref Reused)
        };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await mongo.EnsureIndexesAsync(stoppingToken);

        // Re-queue jobs interrupted by a restart; their frontier state survives in Mongo.
        foreach (var job in await jobs.GetUnfinishedAsync(stoppingToken))
        {
            logger.LogInformation("recovering unfinished job {JobId} ({Topic})", job.Id, job.Topic);
            queue.Enqueue(job.Id);
        }

        var gate = new SemaphoreSlim(MaxParallelJobs);
        var running = new List<Task>();
        await foreach (var jobId in queue.ReadAllAsync(stoppingToken))
        {
            await gate.WaitAsync(stoppingToken);
            running.RemoveAll(t => t.IsCompleted);
            running.Add(Task.Run(async () =>
            {
                try { await RunJobAsync(jobId, stoppingToken); }
                finally { gate.Release(); }
            }, stoppingToken));
        }
        await Task.WhenAll(running);
    }

    private async Task RunJobAsync(string jobId, CancellationToken stoppingToken)
    {
        var job = await jobs.GetAsync(jobId, stoppingToken);
        if (job is null) return;
        if (job.CancelRequested)
        {
            await jobs.SetStatusAsync(jobId, JobStatus.Cancelled, ct: stoppingToken);
            return;
        }

        var scorer = new RelevanceScorer(job.Topic, job.Keywords);
        await jobs.SetStatusAsync(jobId, JobStatus.Running, ct: stoppingToken);
        logger.LogInformation("job {JobId} started: '{Topic}' (keywords: {Keywords})",
            jobId, job.Topic, string.Join(", ", scorer.Keywords));

        try
        {
            await frontier.ReleaseStaleClaimsAsync(jobId, stoppingToken);

            // Seed only fresh jobs; a recovered job still has its frontier.
            if (await frontier.PendingCountAsync(jobId, stoppingToken) == 0 && job.Counts.Fetched == 0)
            {
                var seeds = await seedResolver.ResolveAsync(job.Topic, scorer, job.SeedUrls, stoppingToken);
                if (seeds.Count == 0)
                {
                    await jobs.SetStatusAsync(jobId, JobStatus.Failed, "NoSeedsFound", stoppingToken);
                    return;
                }
                foreach (var seed in seeds)
                    await frontier.EnqueueAsync(jobId, seed.Url, seed.CanonicalUrl, seed.Score, 0, stoppingToken);
                await jobs.SetSeedsAsync(jobId, new SeedBreakdown
                {
                    CustomSeeds = seeds.Count(s => s.Source == SeedSource.Custom),
                    IndexSeeds = seeds.Count(s => s.Source == SeedSource.Index),
                    HubSeeds = seeds.Count(s => s.Source == SeedSource.Hub)
                }, [.. scorer.Keywords], stoppingToken);
            }

            var state = new JobRuntime
            {
                Fetched = job.Counts.Fetched, Stored = job.Counts.Stored,
                Skipped = job.Counts.Skipped, Failed = job.Counts.Failed, Reused = job.Counts.Reused
            };

            using var jobCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            jobCts.CancelAfter(TimeSpan.FromMinutes(job.Config.MaxRuntimeMinutes));

            var workers = Enumerable.Range(0, job.Config.Workers)
                .Select(_ => WorkerLoopAsync(job, scorer, state, jobCts.Token))
                .ToArray();
            var monitor = MonitorAsync(jobId, state, jobCts, stoppingToken);

            await Task.WhenAll(workers);
            jobCts.Cancel(); // stop the monitor
            try { await monitor; } catch (OperationCanceledException) { }

            await jobs.UpdateProgressAsync(jobId, state.Snapshot(), stoppingToken);
            var cancelled = await jobs.IsCancelRequestedAsync(jobId, stoppingToken);
            await jobs.SetStatusAsync(jobId, cancelled ? JobStatus.Cancelled : JobStatus.Completed, ct: stoppingToken);
            logger.LogInformation("job {JobId} finished: stored={Stored} fetched={Fetched} reused={Reused} skipped={Skipped} failed={Failed}",
                jobId, state.Stored, state.Fetched, state.Reused, state.Skipped, state.Failed);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown: leave the job as Running; it is re-queued on next start.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "job {JobId} failed", jobId);
            await jobs.SetStatusAsync(jobId, JobStatus.Failed, ex.Message, CancellationToken.None);
        }
    }

    private async Task WorkerLoopAsync(Job job, RelevanceScorer scorer, JobRuntime state, CancellationToken ct)
    {
        var emptyPolls = 0;
        while (!ct.IsCancellationRequested && Volatile.Read(ref state.Stored) < job.Config.MaxPages)
        {
            // Increment before claiming so a just-claimed item is never invisible to
            // other workers' "frontier drained?" check.
            Interlocked.Increment(ref state.InFlight);
            FrontierItem? item = null;
            try
            {
                item = await frontier.ClaimNextAsync(job.Id, ct);
                if (item is null)
                {
                    Interlocked.Decrement(ref state.InFlight);
                    if (Volatile.Read(ref state.InFlight) == 0 && ++emptyPolls >= 3)
                        return; // frontier drained and nobody is producing
                    await Task.Delay(500, ct);
                    continue;
                }
                emptyPolls = 0;
                var ok = await ProcessItemAsync(job, scorer, state, item, ct);
                await frontier.CompleteAsync(item.Id, ok, CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref state.Failed);
                logger.LogWarning("worker error on {Url}: {Error}", item?.Url, ex.Message);
                if (item is not null)
                    await frontier.CompleteAsync(item.Id, false, CancellationToken.None);
            }
            finally
            {
                if (item is not null)
                    Interlocked.Decrement(ref state.InFlight);
            }
        }
    }

    private async Task<bool> ProcessItemAsync(Job job, RelevanceScorer scorer, JobRuntime state,
        FrontierItem item, CancellationToken ct)
    {
        // Cross-job reuse: a fresh copy in the index means no network round trip at all.
        var existing = await pages.GetByCanonicalAsync(item.CanonicalUrl, ct);
        if (existing is not null &&
            existing.FetchedAt > DateTime.UtcNow.AddDays(-job.Config.ReuseFreshDays))
        {
            Interlocked.Increment(ref state.Reused);
            var cachedPage = new ExtractedPage(existing.Url, existing.Title, "", existing.Text,
                [.. existing.OutboundLinks.Select(l => new ExtractedLink(l.Url, l.Anchor, ""))]);
            var cachedScore = scorer.ScorePage(cachedPage);
            if (cachedScore >= job.Config.MinScoreToStore)
            {
                await pages.AddJobRefAsync(item.CanonicalUrl,
                    new JobRef { JobId = job.Id, Score = cachedScore, Depth = item.Depth }, ct);
                Interlocked.Increment(ref state.Stored);
            }
            else
            {
                Interlocked.Increment(ref state.Skipped);
            }
            await ExpandLinksAsync(job, scorer, item, cachedPage, cachedScore, ct);
            return true;
        }

        var uri = new Uri(item.Url);
        var rules = await robots.GetAsync(uri, ct);
        if (!rules.IsAllowed(uri.PathAndQuery))
        {
            Interlocked.Increment(ref state.Skipped);
            return true;
        }

        var delay = TimeSpan.FromMilliseconds(job.Config.PerHostDelayMs);
        if (rules.CrawlDelaySeconds is { } crawlDelay)
        {
            var requested = TimeSpan.FromSeconds(crawlDelay);
            delay = requested > MaxCrawlDelay ? MaxCrawlDelay : (requested > delay ? requested : delay);
        }
        await rateLimiter.WaitAsync(uri.Host, delay, ct);

        var result = await fetcher.FetchAsync(item.Url, ct);
        if (!result.Success)
        {
            Interlocked.Increment(ref state.Failed);
            logger.LogDebug("fetch failed {Url}: {Error}", item.Url, result.Error);
            return false;
        }

        Interlocked.Increment(ref state.Fetched);
        var page = await extractor.ExtractAsync(result.Html!, result.FinalUrl ?? item.Url, ct);
        var score = scorer.ScorePage(page);

        if (score >= job.Config.MinScoreToStore && page.Text.Length >= MinTextLength)
        {
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(page.Text)));
            if (await pages.DuplicateContentExistsAsync(hash, item.CanonicalUrl, ct))
            {
                Interlocked.Increment(ref state.Skipped);
            }
            else
            {
                await pages.UpsertAsync(new PageDoc
                {
                    CanonicalUrl = item.CanonicalUrl,
                    Url = page.Url,
                    Title = page.Title,
                    Text = page.Text,
                    OutboundLinks = [.. page.Links.Take(MaxLinksPerPage).Select(l => new OutboundLink { Url = l.Url, Anchor = l.Anchor })],
                    ContentHash = hash,
                    FetchedAt = DateTime.UtcNow
                }, new JobRef { JobId = job.Id, Score = score, Depth = item.Depth }, ct);
                Interlocked.Increment(ref state.Stored);
            }
        }
        else
        {
            Interlocked.Increment(ref state.Skipped);
        }

        await ExpandLinksAsync(job, scorer, item, page, score, ct);
        return true;
    }

    /// <summary>Shark-search expansion: enqueue links predicted relevant; at shallow depth
    /// also expand the best few sub-threshold links so cold starts from generic hubs
    /// can tunnel toward the topic.</summary>
    private async Task ExpandLinksAsync(Job job, RelevanceScorer scorer, FrontierItem item,
        ExtractedPage page, double pageScore, CancellationToken ct)
    {
        var childDepth = item.Depth + 1;
        if (childDepth > job.Config.MaxDepth)
            return;

        var scored = page.Links
            .Where(l => l.Url != item.CanonicalUrl)
            .Select(l => (Link: l, Score: scorer.ScoreLink(l.Anchor, l.Context, pageScore, childDepth)))
            .OrderByDescending(x => x.Score)
            .ToList();

        var selected = scored.Where(x => x.Score >= job.Config.MinScoreToExpand).Take(MaxLinksPerPage).ToList();
        if (item.Depth < job.Config.ExplorationDepth)
        {
            selected = selected
                .Concat(scored.Where(x => x.Score < job.Config.MinScoreToExpand).Take(job.Config.ExplorationLinksPerPage))
                .ToList();
        }

        foreach (var (link, linkScore) in selected)
            await frontier.EnqueueAsync(job.Id, link.Url, link.Url, linkScore, childDepth, ct);
    }

    private async Task MonitorAsync(string jobId, JobRuntime state, CancellationTokenSource jobCts,
        CancellationToken stoppingToken)
    {
        while (!jobCts.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), jobCts.Token);
            await jobs.UpdateProgressAsync(jobId, state.Snapshot(), stoppingToken);
            await frontier.ReleaseStaleClaimsAsync(jobId, stoppingToken);
            if (await jobs.IsCancelRequestedAsync(jobId, stoppingToken))
            {
                logger.LogInformation("job {JobId} cancellation requested", jobId);
                jobCts.Cancel();
            }
        }
    }
}
