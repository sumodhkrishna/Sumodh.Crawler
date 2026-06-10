using Sumodh.Crawler.Core.Models;

namespace Sumodh.Crawler.Core.Scoring;

/// <summary>Scores fetched pages for storage and predicts link relevance before fetching
/// (shark-search). Both scores are 0..1.</summary>
public sealed class RelevanceScorer
{
    private static readonly HashSet<string> Stopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "and", "are", "as", "at", "be", "but", "by", "for", "from", "has", "have",
        "how", "in", "into", "is", "it", "its", "of", "on", "or", "that", "the", "their",
        "this", "to", "was", "what", "when", "where", "which", "who", "why", "will", "with"
    };

    /// <summary>Tokenize the topic into keywords; falls back to all tokens if everything is a stopword.</summary>
    public static List<string> DeriveKeywords(string topic, IEnumerable<string>? extra = null)
    {
        var tokens = Tokenize(topic);
        var keywords = tokens.Where(t => !Stopwords.Contains(t) && t.Length > 1).Distinct().ToList();
        if (keywords.Count == 0) keywords = tokens.Distinct().ToList();
        if (extra is not null)
            keywords = keywords.Concat(extra.SelectMany(Tokenize)).Distinct().ToList();
        return keywords;
    }

    private readonly string _phrase;
    private readonly List<string> _keywords;

    public RelevanceScorer(string topic, IEnumerable<string>? extraKeywords = null)
    {
        _phrase = topic.Trim().ToLowerInvariant();
        _keywords = DeriveKeywords(topic, extraKeywords);
    }

    public IReadOnlyList<string> Keywords => _keywords;

    /// <summary>Content relevance of a fetched page.</summary>
    public double ScorePage(ExtractedPage page)
    {
        var titleCoverage = KeywordCoverage(page.Title);
        var h1Coverage = KeywordCoverage(page.H1);
        var bodyDensity = BodyDensity(page.Text);

        var score = 0.40 * titleCoverage + 0.15 * h1Coverage + 0.45 * bodyDensity;

        // Exact phrase is a much stronger signal than bag-of-words overlap.
        if (_phrase.Length > 0 && _keywords.Count > 1)
        {
            if (page.Title.Contains(_phrase, StringComparison.OrdinalIgnoreCase)) score += 0.25;
            else if (page.Text.Contains(_phrase, StringComparison.OrdinalIgnoreCase)) score += 0.15;
        }

        return Math.Clamp(score, 0, 1);
    }

    /// <summary>Predicted relevance of a link before fetching it: anchor text dominates,
    /// context and the (decayed) parent page score fill in, depth applies a penalty.</summary>
    public double ScoreLink(string anchor, string context, double parentScore, int depth)
    {
        var anchorCoverage = KeywordCoverage(anchor);
        var contextCoverage = KeywordCoverage(context);

        var score = 0.50 * anchorCoverage + 0.15 * contextCoverage + 0.35 * parentScore;

        if (_keywords.Count > 1 && anchor.Contains(_phrase, StringComparison.OrdinalIgnoreCase))
            score += 0.25;

        score *= 1.0 / (1.0 + 0.12 * depth);
        return Math.Clamp(score, 0, 1);
    }

    /// <summary>Fraction of topic keywords present in the text (prefix-tolerant, so
    /// "quantum" matches "Quantum mechanics" and plural forms).</summary>
    private double KeywordCoverage(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || _keywords.Count == 0) return 0;
        var tokens = Tokenize(text);
        if (tokens.Count == 0) return 0;
        var hits = _keywords.Count(k => tokens.Any(t => Matches(t, k)));
        return (double)hits / _keywords.Count;
    }

    /// <summary>Keyword occurrences per body word, saturating around 2 per 100 words.</summary>
    private double BodyDensity(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        var tokens = Tokenize(text);
        if (tokens.Count == 0) return 0;
        var hits = tokens.Count(t => _keywords.Any(k => Matches(t, k)));
        var per100 = 100.0 * hits / tokens.Count;
        return Math.Min(1.0, per100 / 2.0);
    }

    private static bool Matches(string token, string keyword) =>
        token.StartsWith(keyword, StringComparison.OrdinalIgnoreCase) ||
        keyword.StartsWith(token, StringComparison.OrdinalIgnoreCase) && token.Length >= 4;

    private static List<string> Tokenize(string text) =>
        text.ToLowerInvariant()
            .Split([' ', '\t', '\r', '\n', '.', ',', ';', ':', '!', '?', '(', ')', '[', ']',
                    '{', '}', '"', '\'', '/', '\\', '|', '-', '_', '–', '—', '·', '•', '#', '&', '='],
                StringSplitOptions.RemoveEmptyEntries)
            .ToList();
}
