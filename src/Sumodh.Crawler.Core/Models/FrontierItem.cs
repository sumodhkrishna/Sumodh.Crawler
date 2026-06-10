using MongoDB.Bson.Serialization.Attributes;

namespace Sumodh.Crawler.Core.Models;

public static class FrontierStatus
{
    public const string Pending = "pending";
    public const string Claimed = "claimed";
    public const string Done = "done";
    public const string Failed = "failed";
}

[BsonIgnoreExtraElements]
public sealed class FrontierItem
{
    [BsonId]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string JobId { get; set; } = "";
    public string Url { get; set; } = "";
    public string CanonicalUrl { get; set; } = "";

    /// <summary>Predicted relevance (shark-search) — the dequeue priority.</summary>
    public double Score { get; set; }

    public int Depth { get; set; }
    public string Status { get; set; } = FrontierStatus.Pending;
    public DateTime? ClaimedAt { get; set; }
}
