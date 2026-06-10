using Sumodh.Crawler.Core.Models;
using Sumodh.Crawler.Core.Storage;
using MongoDB.Driver;

namespace Sumodh.Crawler.Core.Frontier;

/// <summary>Mongo-backed priority frontier. The unique (JobId, CanonicalUrl) index doubles
/// as the per-job URL dedup gate; workers claim items atomically so the crawl is
/// restartable mid-flight.</summary>
public sealed class MongoFrontier(MongoContext ctx)
{
    private static readonly TimeSpan ClaimTimeout = TimeSpan.FromMinutes(2);

    /// <summary>Returns true if the URL was new for this job.</summary>
    public async Task<bool> EnqueueAsync(string jobId, string url, string canonicalUrl, double score, int depth,
        CancellationToken ct = default)
    {
        try
        {
            await ctx.Frontier.InsertOneAsync(new FrontierItem
            {
                JobId = jobId,
                Url = url,
                CanonicalUrl = canonicalUrl,
                Score = score,
                Depth = depth
            }, cancellationToken: ct);
            return true;
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }

    /// <summary>Atomically claims the highest-scored pending item, or null when none remain.</summary>
    public Task<FrontierItem?> ClaimNextAsync(string jobId, CancellationToken ct = default) =>
        ctx.Frontier.FindOneAndUpdateAsync(
            Builders<FrontierItem>.Filter.Eq(f => f.JobId, jobId) &
            Builders<FrontierItem>.Filter.Eq(f => f.Status, FrontierStatus.Pending),
            Builders<FrontierItem>.Update
                .Set(f => f.Status, FrontierStatus.Claimed)
                .Set(f => f.ClaimedAt, DateTime.UtcNow),
            new FindOneAndUpdateOptions<FrontierItem, FrontierItem?>
            {
                Sort = Builders<FrontierItem>.Sort.Descending(f => f.Score),
                ReturnDocument = ReturnDocument.After
            }, ct);

    public Task CompleteAsync(string itemId, bool success, CancellationToken ct = default) =>
        ctx.Frontier.UpdateOneAsync(f => f.Id == itemId,
            Builders<FrontierItem>.Update.Set(f => f.Status, success ? FrontierStatus.Done : FrontierStatus.Failed),
            cancellationToken: ct);

    /// <summary>Items claimed by a worker that died revert to pending after a timeout.</summary>
    public Task ReleaseStaleClaimsAsync(string jobId, CancellationToken ct = default) =>
        ctx.Frontier.UpdateManyAsync(
            f => f.JobId == jobId && f.Status == FrontierStatus.Claimed
                 && f.ClaimedAt < DateTime.UtcNow - ClaimTimeout,
            Builders<FrontierItem>.Update
                .Set(f => f.Status, FrontierStatus.Pending)
                .Set(f => f.ClaimedAt, null),
            cancellationToken: ct);

    public Task<long> PendingCountAsync(string jobId, CancellationToken ct = default) =>
        ctx.Frontier.CountDocumentsAsync(
            f => f.JobId == jobId && f.Status == FrontierStatus.Pending, cancellationToken: ct);

    public Task ClearJobAsync(string jobId, CancellationToken ct = default) =>
        ctx.Frontier.DeleteManyAsync(f => f.JobId == jobId, cancellationToken: ct);
}
