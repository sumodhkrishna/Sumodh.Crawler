using MongoDB.Bson.Serialization.Attributes;

namespace Sumodh.Crawler.Core.Models;

public enum JobStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled
}

public sealed class JobCounts
{
    public long Fetched { get; set; }
    public long Stored { get; set; }
    public long Skipped { get; set; }
    public long Failed { get; set; }
    public long Reused { get; set; }
}

public sealed class SeedBreakdown
{
    public int CustomSeeds { get; set; }
    public int IndexSeeds { get; set; }
    public int HubSeeds { get; set; }
}

[BsonIgnoreExtraElements]
public sealed class Job
{
    [BsonId]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Topic { get; set; } = "";
    public List<string> Keywords { get; set; } = [];

    /// <summary>Optional caller-supplied starting URLs; always combined with index + hub seeds.</summary>
    public List<string> SeedUrls { get; set; } = [];

    [BsonRepresentation(MongoDB.Bson.BsonType.String)]
    public JobStatus Status { get; set; } = JobStatus.Pending;

    public JobCounts Counts { get; set; } = new();
    public SeedBreakdown Seeds { get; set; } = new();
    public CrawlConfig Config { get; set; } = new();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public string? Error { get; set; }
    public bool CancelRequested { get; set; }
}
