using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Sumodh.Crawler.Core.Models;
using Sumodh.Crawler.Core.Urls;

namespace Sumodh.Crawler.Core.Extraction;

/// <summary>Turns raw HTML into clean text plus outbound links annotated with the
/// anchor text and surrounding context that shark-search link scoring needs.</summary>
public sealed class HtmlExtractor
{
    private const int ContextWindowChars = 160;
    private static readonly string[] ChromeSelectors =
        ["script", "style", "noscript", "svg", "iframe", "nav", "footer", "aside", "form"];

    private readonly IBrowsingContext _browsing = BrowsingContext.New(Configuration.Default);

    public async Task<ExtractedPage> ExtractAsync(string html, string url, CancellationToken ct = default)
    {
        var document = await _browsing.OpenAsync(req => req.Content(html).Address(url), ct);

        var title = document.Title?.Trim() ?? "";
        var h1 = document.QuerySelector("h1")?.TextContent.Trim() ?? "";

        // Collect links (with context) before stripping chrome, so nav links still count
        // for traversal — but extract text after stripping, so chrome doesn't pollute scoring.
        var links = new List<ExtractedLink>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in document.QuerySelectorAll("a[href]").OfType<IHtmlAnchorElement>())
        {
            var href = element.Href;
            if (string.IsNullOrWhiteSpace(href)) continue;
            var canonical = UrlCanonicalizer.Canonicalize(href);
            if (canonical is null || !seen.Add(canonical)) continue;

            var anchor = Normalize(element.TextContent);
            if (anchor.Length > 300) anchor = anchor[..300];

            var parentText = Normalize(element.ParentElement?.TextContent ?? "");
            var context = parentText.Length > ContextWindowChars
                ? FocusContext(parentText, anchor)
                : parentText;

            links.Add(new ExtractedLink(canonical, anchor, context));
        }

        foreach (var node in document.QuerySelectorAll(string.Join(",", ChromeSelectors)))
            node.Remove();

        var text = Normalize(document.Body?.TextContent ?? "");

        return new ExtractedPage(url, title, h1, text, links);
    }

    /// <summary>Window of the parent's text centered on the anchor occurrence.</summary>
    private static string FocusContext(string parentText, string anchor)
    {
        var idx = anchor.Length > 0 ? parentText.IndexOf(anchor, StringComparison.Ordinal) : -1;
        if (idx < 0) return parentText[..ContextWindowChars];
        var start = Math.Max(0, idx - ContextWindowChars / 2);
        var length = Math.Min(parentText.Length - start, anchor.Length + ContextWindowChars);
        return parentText.Substring(start, length);
    }

    private static string Normalize(string text) =>
        string.Join(' ', text.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
}
