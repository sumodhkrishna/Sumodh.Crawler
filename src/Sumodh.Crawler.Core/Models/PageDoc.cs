using MongoDB.Bson.Serialization.Attributes;

namespace Sumodh.Crawler.Core.Models;

public sealed class OutboundLink
{
    public string Url { get; set; } = "";
    public string Anchor { get; set; } = "";
}

public sealed class JobRef
{
    public string JobId { get; set; } = "";
    public double Score { get; set; }
    public int Depth { get; set; }
}

/// <summary>A stored page. Shared across jobs — this collection, with its text index,
/// is the persistent index that warms up future crawls.</summary>
[BsonIgnoreExtraElements]
public sealed class PageDoc
{
    [BsonId]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string CanonicalUrl { get; set; } = "";
    public string Url { get; set; } = "";
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    public List<OutboundLink> OutboundLinks { get; set; } = [];
    public string ContentHash { get; set; } = "";
    public DateTime FetchedAt { get; set; }
    public List<JobRef> JobRefs { get; set; } = [];
}
