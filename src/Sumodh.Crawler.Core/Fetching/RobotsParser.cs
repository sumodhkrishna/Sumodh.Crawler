namespace Sumodh.Crawler.Core.Fetching;

public sealed class RobotsRules
{
    public List<(string Path, bool Allow)> Rules { get; init; } = [];
    public double? CrawlDelaySeconds { get; init; }

    public static readonly RobotsRules AllowAll = new();

    /// <summary>Longest-matching rule wins (Google semantics); no match means allowed.</summary>
    public bool IsAllowed(string path)
    {
        if (string.IsNullOrEmpty(path)) path = "/";
        (int Length, bool Allow)? best = null;
        foreach (var (rulePath, allow) in Rules)
        {
            if (PathMatches(rulePath, path) && (best is null || rulePath.Length > best.Value.Length))
                best = (rulePath.Length, allow);
        }
        return best?.Allow ?? true;
    }

    private static bool PathMatches(string pattern, string path)
    {
        // Prefix match with '*' wildcard and '$' end anchor.
        var anchored = pattern.EndsWith('$');
        if (anchored) pattern = pattern[..^1];

        var segments = pattern.Split('*');
        var pos = 0;
        for (var i = 0; i < segments.Length; i++)
        {
            var seg = segments[i];
            if (seg.Length == 0) continue;
            var idx = i == 0
                ? (path.StartsWith(seg, StringComparison.Ordinal) ? 0 : -1)
                : path.IndexOf(seg, pos, StringComparison.Ordinal);
            if (idx < 0) return false;
            pos = idx + seg.Length;
        }
        return !anchored || pos == path.Length || pattern.EndsWith('*');
    }
}

/// <summary>Minimal robots.txt parser: User-agent groups, Allow/Disallow, Crawl-delay.</summary>
public static class RobotsParser
{
    public static RobotsRules Parse(string content, string userAgent)
    {
        var agentToken = userAgent.ToLowerInvariant();
        var rules = new List<(string, bool)>();
        var wildcardRules = new List<(string, bool)>();
        double? delay = null, wildcardDelay = null;

        var inMatchingGroup = false;
        var inWildcardGroup = false;
        var lastLineWasAgent = false;

        foreach (var raw in content.Split('\n'))
        {
            var line = raw;
            var hash = line.IndexOf('#');
            if (hash >= 0) line = line[..hash];
            line = line.Trim();
            if (line.Length == 0) continue;

            var colon = line.IndexOf(':');
            if (colon < 0) continue;
            var field = line[..colon].Trim().ToLowerInvariant();
            var value = line[(colon + 1)..].Trim();

            if (field == "user-agent")
            {
                // A run of consecutive User-agent lines starts a fresh group.
                if (!lastLineWasAgent)
                {
                    inMatchingGroup = false;
                    inWildcardGroup = false;
                }
                var agent = value.ToLowerInvariant();
                if (agent == "*") inWildcardGroup = true;
                else if (agentToken.Contains(agent)) inMatchingGroup = true;
                lastLineWasAgent = true;
                continue;
            }
            lastLineWasAgent = false;

            switch (field)
            {
                case "disallow" or "allow":
                    if (value.Length == 0 && field == "disallow") break; // empty Disallow = allow all
                    var rule = (value, field == "allow");
                    if (inMatchingGroup) rules.Add(rule);
                    else if (inWildcardGroup) wildcardRules.Add(rule);
                    break;
                case "crawl-delay":
                    if (double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var d))
                    {
                        if (inMatchingGroup) delay = d;
                        else if (inWildcardGroup) wildcardDelay = d;
                    }
                    break;
            }
        }

        // A group naming us specifically overrides the wildcard group entirely.
        return rules.Count > 0 || delay is not null
            ? new RobotsRules { Rules = rules, CrawlDelaySeconds = delay ?? wildcardDelay }
            : new RobotsRules { Rules = wildcardRules, CrawlDelaySeconds = wildcardDelay };
    }
}
