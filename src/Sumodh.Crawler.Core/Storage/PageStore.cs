using Sumodh.Crawler.Core.Models;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Sumodh.Crawler.Core.Storage;

public sealed class PageStore(MongoContext ctx)
{
    public Task<PageDoc?> GetByCanonicalAsync(string canonicalUrl, CancellationToken ct = default) =>
        ctx.Pages.Find(p => p.CanonicalUrl == canonicalUrl).FirstOrDefaultAsync(ct)!;

    /// <summary>True when the same extracted text already exists under a different URL (mirror/duplicate).</summary>
    public Task<bool> DuplicateContentExistsAsync(string hash, string canonicalUrl, CancellationToken ct = default) =>
        ctx.Pages.Find(p => p.ContentHash == hash && p.CanonicalUrl != canonicalUrl).AnyAsync(ct);

    /// <summary>Insert the page or, if the canonical URL is already indexed, refresh it and append the job ref.</summary>
    public async Task UpsertAsync(PageDoc page, JobRef jobRef, CancellationToken ct = default)
    {
        var filter = Builders<PageDoc>.Filter.Eq(p => p.CanonicalUrl, page.CanonicalUrl);
        var update = Builders<PageDoc>.Update
            .Set(p => p.Url, page.Url)
            .Set(p => p.Title, page.Title)
            .Set(p => p.Text, page.Text)
            .Set(p => p.OutboundLinks, page.OutboundLinks)
            .Set(p => p.ContentHash, page.ContentHash)
            .Set(p => p.FetchedAt, page.FetchedAt)
            .SetOnInsert(p => p.Id, page.Id)
            .Push(p => p.JobRefs, jobRef);
        await ctx.Pages.UpdateOneAsync(filter, update, new UpdateOptions { IsUpsert = true }, ct);
    }

    /// <summary>Tag an already-indexed page as relevant to another job (cross-job reuse, no re-fetch).</summary>
    public Task AddJobRefAsync(string canonicalUrl, JobRef jobRef, CancellationToken ct = default) =>
        ctx.Pages.UpdateOneAsync(
            Builders<PageDoc>.Filter.Eq(p => p.CanonicalUrl, canonicalUrl) &
            Builders<PageDoc>.Filter.Not(Builders<PageDoc>.Filter.ElemMatch(p => p.JobRefs, r => r.JobId == jobRef.JobId)),
            Builders<PageDoc>.Update.Push(p => p.JobRefs, jobRef),
            cancellationToken: ct);

    public Task<List<PageDoc>> GetJobPagesAsync(string jobId, int limit, int skip, CancellationToken ct = default) =>
        ctx.Pages.Find(Builders<PageDoc>.Filter.ElemMatch(p => p.JobRefs, r => r.JobId == jobId))
            .Sort(Builders<PageDoc>.Sort.Descending("JobRefs.Score"))
            .Skip(skip).Limit(limit)
            .ToListAsync(ct);

    /// <summary>Ranked full-text search over the whole index — the "search engine" face of the system,
    /// and the source of warm seeds for new jobs.</summary>
    public Task<List<PageDoc>> SearchAsync(string query, int limit, CancellationToken ct = default)
    {
        var filter = Builders<PageDoc>.Filter.Text(query);
        var projection = Builders<PageDoc>.Projection.MetaTextScore("score");
        return ctx.Pages.Find(filter)
            .Project<PageDoc>(projection)
            .Sort(Builders<PageDoc>.Sort.MetaTextScore("score"))
            .Limit(limit)
            .ToListAsync(ct);
    }

    public Task<long> CountJobPagesAsync(string jobId, CancellationToken ct = default) =>
        ctx.Pages.CountDocumentsAsync(
            Builders<PageDoc>.Filter.ElemMatch(p => p.JobRefs, r => r.JobId == jobId), cancellationToken: ct);
}
