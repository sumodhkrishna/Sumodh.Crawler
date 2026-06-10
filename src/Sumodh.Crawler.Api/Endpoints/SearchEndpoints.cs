using Sumodh.Crawler.Core.Storage;

namespace Sumodh.Crawler.Api.Endpoints;

public static class SearchEndpoints
{
    /// <summary>Ranked full-text search over everything ever crawled — the persistent
    /// index queried directly, no crawl required.</summary>
    public static void MapSearch(this IEndpointRouteBuilder app)
    {
        app.MapGet("/search", async (string q, PageStore pages, int limit = 20) =>
        {
            if (string.IsNullOrWhiteSpace(q))
                return Results.BadRequest(new { error = "q is required" });

            var hits = await pages.SearchAsync(q, Math.Clamp(limit, 1, 100));
            return Results.Ok(hits.Select(p => new
            {
                url = p.Url,
                title = p.Title,
                fetchedAt = p.FetchedAt,
                snippet = p.Text.Length > 300 ? p.Text[..300] + "…" : p.Text
            }));
        })
        .WithTags("Search")
        .WithSummary("Search the persistent index")
        .WithDescription("Ranked full-text search over every page ever crawled, across all jobs — no crawl is triggered.");
    }
}
