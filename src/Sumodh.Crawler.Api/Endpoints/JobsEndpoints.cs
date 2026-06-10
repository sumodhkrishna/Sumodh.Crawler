using Sumodh.Crawler.Core.Models;
using Sumodh.Crawler.Core.Orchestration;
using Sumodh.Crawler.Core.Storage;

namespace Sumodh.Crawler.Api.Endpoints;

public sealed record CreateJobRequest(
    string Topic,
    List<string>? Keywords,
    List<string>? Urls,
    int? MaxPages,
    int? MaxDepth,
    int? Workers,
    double? MinScoreToStore,
    double? MinScoreToExpand,
    int? MaxRuntimeMinutes);

public static class JobsEndpoints
{
    public static void MapJobs(this IEndpointRouteBuilder app, CrawlConfig defaults)
    {
        var group = app.MapGroup("/jobs").WithTags("Jobs");

        group.MapPost("/", async (CreateJobRequest request, JobStore jobs, JobQueue queue) =>
        {
            if (string.IsNullOrWhiteSpace(request.Topic))
                return Results.BadRequest(new { error = "topic is required" });

            var job = new Job
            {
                Topic = request.Topic.Trim(),
                Keywords = request.Keywords ?? [],
                SeedUrls = request.Urls ?? [],
                Config = new CrawlConfig
                {
                    MaxPages = request.MaxPages ?? defaults.MaxPages,
                    MaxDepth = request.MaxDepth ?? defaults.MaxDepth,
                    Workers = request.Workers ?? defaults.Workers,
                    MinScoreToStore = request.MinScoreToStore ?? defaults.MinScoreToStore,
                    MinScoreToExpand = request.MinScoreToExpand ?? defaults.MinScoreToExpand,
                    MaxRuntimeMinutes = request.MaxRuntimeMinutes ?? defaults.MaxRuntimeMinutes,
                    PerHostDelayMs = defaults.PerHostDelayMs,
                    ExplorationDepth = defaults.ExplorationDepth,
                    ExplorationLinksPerPage = defaults.ExplorationLinksPerPage,
                    ReuseFreshDays = defaults.ReuseFreshDays
                }
            };
            await jobs.InsertAsync(job);
            queue.Enqueue(job.Id);
            return Results.Accepted($"/jobs/{job.Id}", new { jobId = job.Id });
        })
        .WithSummary("Start a crawl job")
        .WithDescription("Accepts a topic and returns a job id immediately (202); the crawl then runs autonomously. Only 'topic' is required — every other field falls back to the configured defaults. 'urls' optionally adds your own starting URLs, crawled at top priority; public-web discovery (index + hub seeds) still always runs alongside them.");

        group.MapGet("/{id}", async (string id, JobStore jobs) =>
        {
            var job = await jobs.GetAsync(id);
            return job is null
                ? Results.NotFound()
                : Results.Ok(new
                {
                    jobId = job.Id,
                    topic = job.Topic,
                    keywords = job.Keywords,
                    status = job.Status.ToString(),
                    counts = job.Counts,
                    seeds = job.Seeds,
                    createdAt = job.CreatedAt,
                    startedAt = job.StartedAt,
                    finishedAt = job.FinishedAt,
                    runtimeSeconds = job.StartedAt is { } s
                        ? ((job.FinishedAt ?? DateTime.UtcNow) - s).TotalSeconds
                        : (double?)null,
                    error = job.Error
                });
        })
        .WithSummary("Get job status and progress")
        .WithDescription("Status, page counters (fetched/stored/skipped/failed/reused), and how many seeds came from the persistent index vs the bundled hub list.");

        group.MapGet("/{id}/pages", async (string id, PageStore pages, int limit = 20, int skip = 0) =>
        {
            var docs = await pages.GetJobPagesAsync(id, Math.Clamp(limit, 1, 200), Math.Max(skip, 0));
            return Results.Ok(docs.Select(p =>
            {
                var jobRef = p.JobRefs.FirstOrDefault(r => r.JobId == id);
                return new
                {
                    url = p.Url,
                    title = p.Title,
                    score = jobRef?.Score ?? 0,
                    depth = jobRef?.Depth ?? 0,
                    fetchedAt = p.FetchedAt,
                    snippet = p.Text.Length > 300 ? p.Text[..300] + "…" : p.Text
                };
            }).OrderByDescending(p => p.score));
        })
        .WithSummary("Get a job's crawled pages")
        .WithDescription("Pages stored for this job, most relevant first.");

        group.MapPost("/{id}/cancel", async (string id, JobStore jobs) =>
        {
            var job = await jobs.GetAsync(id);
            if (job is null) return Results.NotFound();
            await jobs.RequestCancelAsync(id);
            return Results.Accepted($"/jobs/{id}");
        })
        .WithSummary("Cancel a running job")
        .WithDescription("Sets a cancellation flag the orchestrator picks up within a few seconds. Already-stored pages are kept.");
    }
}
