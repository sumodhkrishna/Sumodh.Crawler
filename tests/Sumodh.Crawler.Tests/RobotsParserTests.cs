using Sumodh.Crawler.Core.Fetching;

namespace Sumodh.Crawler.Tests;

public class RobotsParserTests
{
    [Fact]
    public void Disallow_blocks_matching_paths()
    {
        var rules = RobotsParser.Parse("""
            User-agent: *
            Disallow: /private/
            """, "TopicCrawler");

        Assert.False(rules.IsAllowed("/private/page"));
        Assert.True(rules.IsAllowed("/public/page"));
    }

    [Fact]
    public void Longest_match_wins_so_allow_can_carve_out_exceptions()
    {
        var rules = RobotsParser.Parse("""
            User-agent: *
            Disallow: /docs/
            Allow: /docs/public/
            """, "TopicCrawler");

        Assert.False(rules.IsAllowed("/docs/secret"));
        Assert.True(rules.IsAllowed("/docs/public/intro"));
    }

    [Fact]
    public void Specific_agent_group_overrides_wildcard()
    {
        var rules = RobotsParser.Parse("""
            User-agent: *
            Disallow: /

            User-agent: TopicCrawler
            Disallow: /admin/
            """, "TopicCrawler");

        Assert.True(rules.IsAllowed("/anything"));
        Assert.False(rules.IsAllowed("/admin/panel"));
    }

    [Fact]
    public void Crawl_delay_is_parsed()
    {
        var rules = RobotsParser.Parse("""
            User-agent: *
            Crawl-delay: 2.5
            Disallow: /tmp/
            """, "TopicCrawler");

        Assert.Equal(2.5, rules.CrawlDelaySeconds);
    }

    [Fact]
    public void Wildcard_pattern_in_path_matches()
    {
        var rules = RobotsParser.Parse("""
            User-agent: *
            Disallow: /*/print$
            """, "TopicCrawler");

        Assert.False(rules.IsAllowed("/article/print"));
        Assert.True(rules.IsAllowed("/article/print/extra"));
    }

    [Fact]
    public void Empty_or_missing_robots_allows_everything()
    {
        var rules = RobotsParser.Parse("", "TopicCrawler");
        Assert.True(rules.IsAllowed("/anything"));
        Assert.True(RobotsRules.AllowAll.IsAllowed("/x"));
    }

    [Fact]
    public void Comments_and_blank_lines_are_ignored()
    {
        var rules = RobotsParser.Parse("""
            # full line comment
            User-agent: *

            Disallow: /hidden/ # trailing comment
            """, "TopicCrawler");

        Assert.False(rules.IsAllowed("/hidden/x"));
    }
}
