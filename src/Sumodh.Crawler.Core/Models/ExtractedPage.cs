namespace Sumodh.Crawler.Core.Models;

public sealed record ExtractedLink(string Url, string Anchor, string Context);

public sealed record ExtractedPage(
    string Url,
    string Title,
    string H1,
    string Text,
    IReadOnlyList<ExtractedLink> Links);
