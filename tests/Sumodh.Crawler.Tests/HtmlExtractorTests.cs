using Sumodh.Crawler.Core.Extraction;

namespace Sumodh.Crawler.Tests;

public class HtmlExtractorTests
{
    private readonly HtmlExtractor _extractor = new();

    private const string SampleHtml = """
        <html>
        <head><title>Quantum error correction - Test</title>
        <script>var tracking = "should not appear";</script>
        <style>.hidden { display: none; }</style>
        </head>
        <body>
        <nav><a href="/home">Home</a> navigation menu text</nav>
        <h1>Quantum error correction</h1>
        <p>Quantum error correction protects quantum information.
           See the article on <a href="/wiki/Stabilizer_code">stabilizer codes</a> for details.</p>
        <p>External resource: <a href="https://other.example.org/qec?fbclid=track">QEC tutorial</a>.</p>
        <a href="mailto:someone@example.com">email us</a>
        <footer>Copyright footer text</footer>
        </body>
        </html>
        """;

    [Fact]
    public async Task Extracts_title_h1_and_clean_text()
    {
        var page = await _extractor.ExtractAsync(SampleHtml, "https://example.com/wiki/QEC");

        Assert.Equal("Quantum error correction - Test", page.Title);
        Assert.Equal("Quantum error correction", page.H1);
        Assert.Contains("protects quantum information", page.Text);
        Assert.DoesNotContain("should not appear", page.Text);
        Assert.DoesNotContain("display: none", page.Text);
        Assert.DoesNotContain("Copyright footer", page.Text);
    }

    [Fact]
    public async Task Resolves_relative_links_and_canonicalizes()
    {
        var page = await _extractor.ExtractAsync(SampleHtml, "https://example.com/wiki/QEC");

        Assert.Contains(page.Links, l => l.Url == "https://example.com/wiki/Stabilizer_code");
        // Tracking param stripped by canonicalization.
        Assert.Contains(page.Links, l => l.Url == "https://other.example.org/qec");
        Assert.DoesNotContain(page.Links, l => l.Url.StartsWith("mailto:"));
    }

    [Fact]
    public async Task Captures_anchor_text_and_surrounding_context()
    {
        var page = await _extractor.ExtractAsync(SampleHtml, "https://example.com/wiki/QEC");

        var link = page.Links.Single(l => l.Url == "https://example.com/wiki/Stabilizer_code");
        Assert.Equal("stabilizer codes", link.Anchor);
        Assert.Contains("article", link.Context);
    }

    [Fact]
    public async Task Deduplicates_repeated_links()
    {
        const string html = """
            <html><body>
            <a href="/page">first</a>
            <a href="/page/">second points to same canonical</a>
            </body></html>
            """;
        var page = await _extractor.ExtractAsync(html, "https://example.com/");
        Assert.Single(page.Links, l => l.Url == "https://example.com/page");
    }
}
