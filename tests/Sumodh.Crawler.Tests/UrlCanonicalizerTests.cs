using Sumodh.Crawler.Core.Urls;

namespace Sumodh.Crawler.Tests;

public class UrlCanonicalizerTests
{
    [Theory]
    [InlineData("HTTPS://Example.COM/Path/", "https://example.com/Path")]
    [InlineData("http://example.com:80/page", "http://example.com/page")]
    [InlineData("https://example.com:443/page", "https://example.com/page")]
    [InlineData("https://example.com/page#section", "https://example.com/page")]
    [InlineData("https://example.com/", "https://example.com/")]
    public void Normalizes_host_port_path_and_fragment(string input, string expected) =>
        Assert.Equal(expected, UrlCanonicalizer.Canonicalize(input));

    [Fact]
    public void Strips_tracking_params_and_sorts_query()
    {
        var result = UrlCanonicalizer.Canonicalize(
            "https://example.com/a?utm_source=x&b=2&a=1&fbclid=zzz");
        Assert.Equal("https://example.com/a?a=1&b=2", result);
    }

    [Theory]
    [InlineData("mailto:someone@example.com")]
    [InlineData("javascript:void(0)")]
    [InlineData("ftp://example.com/file")]
    [InlineData("not a url")]
    public void Rejects_non_http_urls(string input) =>
        Assert.Null(UrlCanonicalizer.Canonicalize(input));

    [Fact]
    public void Resolves_relative_against_base()
    {
        var result = UrlCanonicalizer.Canonicalize("../other/page", "https://example.com/dir/sub/");
        Assert.Equal("https://example.com/dir/other/page", result);
    }

    [Fact]
    public void Same_page_variants_collapse_to_one_key()
    {
        var a = UrlCanonicalizer.Canonicalize("https://Example.com/wiki/Foo/?utm_campaign=x");
        var b = UrlCanonicalizer.Canonicalize("https://example.com/wiki/Foo");
        Assert.Equal(a, b);
    }
}
