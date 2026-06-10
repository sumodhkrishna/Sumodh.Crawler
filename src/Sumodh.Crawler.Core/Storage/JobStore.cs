using Sumodh.Crawler.Core.Models;
using MongoDB.Driver;

namespace Sumodh.Crawler.Core.Storage;

public sealed class JobStore(MongoContext ctx)
{
    public Task InsertAsync(Job job, CancellationToken ct = default) =>
        ctx.Jobs.InsertOneAsync(job, cancellationToken: ct);

    public Task<Job?> GetAsync(string id, CancellationToken ct = default) =>
        ctx.Jobs.Find(j => j.Id == id).FirstOrDefaultAsync(ct)!;

    public Task<List<Job>> GetUnfinishedAsync(CancellationToken ct = default) =>
        ctx.Jobs.Find(j => j.Status == JobStatus.Pending || j.Status == JobStatus.Running)
            .SortBy(j => j.CreatedAt).ToListAsync(ct);

    public Task SetStatusAsync(string id, JobStatus status, string? error = null, CancellationToken ct = default)
    {
        var update = Builders<Job>.Update.Set(j => j.Status, status);
        update = status switch
        {
            JobStatus.Running => update.Set(j => j.StartedAt, DateTime.UtcNow),
            JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled =>
                update.Set(j => j.FinishedAt, DateTime.UtcNow).Set(j => j.Error, error),
            _ => update
        };
        return ctx.Jobs.UpdateOneAsync(j => j.Id == id, update, cancellationToken: ct);
    }

    public Task UpdateProgressAsync(string id, JobCounts counts, CancellationToken ct = default) =>
        ctx.Jobs.UpdateOneAsync(j => j.Id == id,
            Builders<Job>.Update.Set(j => j.Counts, counts), cancellationToken: ct);

    public Task SetSeedsAsync(string id, SeedBreakdown seeds, List<string> keywords, CancellationToken ct = default) =>
        ctx.Jobs.UpdateOneAsync(j => j.Id == id,
            Builders<Job>.Update.Set(j => j.Seeds, seeds).Set(j => j.Keywords, keywords),
            cancellationToken: ct);

    public Task RequestCancelAsync(string id, CancellationToken ct = default) =>
        ctx.Jobs.UpdateOneAsync(j => j.Id == id,
            Builders<Job>.Update.Set(j => j.CancelRequested, true), cancellationToken: ct);

    public async Task<bool> IsCancelRequestedAsync(string id, CancellationToken ct = default)
    {
        var job = await ctx.Jobs.Find(j => j.Id == id)
            .Project(j => new { j.CancelRequested }).FirstOrDefaultAsync(ct);
        return job?.CancelRequested ?? false;
    }
}
