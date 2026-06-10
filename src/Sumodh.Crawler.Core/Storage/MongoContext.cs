using Sumodh.Crawler.Core.Models;
using MongoDB.Driver;

namespace Sumodh.Crawler.Core.Storage;

/// <summary>Owns the Mongo collections and ensures indexes exist at startup.</summary>
public sealed class MongoContext
{
    public IMongoDatabase Database { get; }
    public IMongoCollection<Job> Jobs { get; }
    public IMongoCollection<PageDoc> Pages { get; }
    public IMongoCollection<FrontierItem> Frontier { get; }

    public MongoContext(IMongoClient client, string databaseName = "crawler")
    {
        Database = client.GetDatabase(databaseName);
        Jobs = Database.GetCollection<Job>("jobs");
        Pages = Database.GetCollection<PageDoc>("pages");
        Frontier = Database.GetCollection<FrontierItem>("frontier");
    }

    public async Task EnsureIndexesAsync(CancellationToken ct = default)
    {
        await Pages.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<PageDoc>(
                Builders<PageDoc>.IndexKeys.Text(p => p.Title).Text(p => p.Text),
                new CreateIndexOptions { Name = "text_index", Weights = new MongoDB.Bson.BsonDocument { { "Title", 5 }, { "Text", 1 } } }),
            new CreateIndexModel<PageDoc>(
                Builders<PageDoc>.IndexKeys.Ascending(p => p.CanonicalUrl),
                new CreateIndexOptions { Unique = true, Name = "canonical_unique" }),
            new CreateIndexModel<PageDoc>(
                Builders<PageDoc>.IndexKeys.Ascending(p => p.ContentHash),
                new CreateIndexOptions { Name = "content_hash" }),
            new CreateIndexModel<PageDoc>(
                Builders<PageDoc>.IndexKeys.Ascending("JobRefs.JobId").Descending("JobRefs.Score"),
                new CreateIndexOptions { Name = "job_refs" })
        ], cancellationToken: ct);

        await Frontier.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<FrontierItem>(
                Builders<FrontierItem>.IndexKeys
                    .Ascending(f => f.JobId).Ascending(f => f.Status).Descending(f => f.Score),
                new CreateIndexOptions { Name = "claim_order" }),
            new CreateIndexModel<FrontierItem>(
                Builders<FrontierItem>.IndexKeys.Ascending(f => f.JobId).Ascending(f => f.CanonicalUrl),
                new CreateIndexOptions { Unique = true, Name = "job_url_unique" })
        ], cancellationToken: ct);

        await Jobs.Indexes.CreateOneAsync(
            new CreateIndexModel<Job>(
                Builders<Job>.IndexKeys.Ascending(j => j.Status).Descending(j => j.CreatedAt),
                new CreateIndexOptions { Name = "status_created" }),
            cancellationToken: ct);
    }
}
