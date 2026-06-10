using Sumodh.Crawler.Core.Models;
using Sumodh.Crawler.Core.Scoring;

namespace Sumodh.Crawler.Tests;

public class RelevanceScorerTests
{
    private static ExtractedPage Page(string title, string h1, string text) =>
        new("https://example.com", title, h1, text, []);

    [Fact]
    public void Derives_keywords_dropping_stopwords()
    {
        var keywords = RelevanceScorer.DeriveKeywords("the history of quantum error correction");
        Assert.Contains("quantum", keywords);
        Assert.Contains("error", keywords);
        Assert.Contains("correction", keywords);
        Assert.Contains("history", keywords);
        Assert.DoesNotContain("the", keywords);
        Assert.DoesNotContain("of", keywords);
    }

    [Fact]
    public void On_topic_page_outscores_off_topic_page()
    {
        var scorer = new RelevanceScorer("quantum error correction");
        var onTopic = Page(
            "Quantum error correction - Wikipedia",
            "Quantum error correction",
            "Quantum error correction is used in quantum computing to protect quantum information from errors due to decoherence and other quantum noise.");
        var offTopic = Page(
            "Banana bread recipes",
            "Banana bread",
            "Mix flour, sugar and ripe bananas. Bake for an hour at 350 degrees until golden.");

        Assert.True(scorer.ScorePage(onTopic) > 0.5);
        Assert.True(scorer.ScorePage(offTopic) < 0.1);
    }

    [Fact]
    public void Exact_phrase_in_title_boosts_score()
    {
        var scorer = new RelevanceScorer("quantum error correction");
        var phrase = Page("Quantum error correction explained", "",
            "An introduction to the field with stabilizer codes and syndrome measurement.");
        var scattered = Page("Correction of common error types in quantum devices", "",
            "An introduction to the field with stabilizer codes and syndrome measurement.");

        Assert.True(scorer.ScorePage(phrase) > scorer.ScorePage(scattered));
    }

    [Fact]
    public void Anchor_match_dominates_link_prediction()
    {
        var scorer = new RelevanceScorer("quantum error correction");
        var topical = scorer.ScoreLink("Quantum error correction codes", "related research areas", 0.2, 1);
        var generic = scorer.ScoreLink("About us", "company information and contact", 0.2, 1);

        Assert.True(topical > generic);
        Assert.True(topical > 0.3);
    }

    [Fact]
    public void Parent_score_propagates_to_links_with_decay()
    {
        var scorer = new RelevanceScorer("quantum error correction");
        var fromGoodParent = scorer.ScoreLink("read more", "", 0.9, 2);
        var fromBadParent = scorer.ScoreLink("read more", "", 0.0, 2);

        Assert.True(fromGoodParent > fromBadParent);
    }

    [Fact]
    public void Deeper_links_score_lower()
    {
        var scorer = new RelevanceScorer("quantum error correction");
        var shallow = scorer.ScoreLink("quantum error correction", "", 0.5, 1);
        var deep = scorer.ScoreLink("quantum error correction", "", 0.5, 6);

        Assert.True(shallow > deep);
    }

    [Fact]
    public void Prefix_matching_tolerates_plurals_and_compounds()
    {
        var scorer = new RelevanceScorer("quantum computer");
        var link = scorer.ScoreLink("Quantum computers and computing", "", 0, 1);
        Assert.True(link > 0.4);
    }
}
