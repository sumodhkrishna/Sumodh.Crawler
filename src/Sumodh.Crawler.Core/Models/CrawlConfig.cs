namespace Sumodh.Crawler.Core.Models;

/// <summary>Per-job crawl settings; defaults come from appsettings and can be overridden per request.</summary>
public sealed class CrawlConfig
{
    public int MaxPages { get; set; } = 500;
    public int MaxDepth { get; set; } = 6;
    public int Workers { get; set; } = 8;
    public double MinScoreToStore { get; set; } = 0.15;
    public double MinScoreToExpand { get; set; } = 0.08;
    public int MaxRuntimeMinutes { get; set; } = 30;
    public int PerHostDelayMs { get; set; } = 1000;

    /// <summary>At depths below this, the top links of each page are expanded even when they
    /// score under MinScoreToExpand — keeps cold starts from generic hubs alive.</summary>
    public int ExplorationDepth { get; set; } = 3;
    public int ExplorationLinksPerPage { get; set; } = 5;

    /// <summary>Pages already in the index fresher than this are reused instead of re-fetched.</summary>
    public int ReuseFreshDays { get; set; } = 7;
}
