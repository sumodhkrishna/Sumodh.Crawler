using System.Web;

namespace Sumodh.Crawler.Core.Urls;

public static class UrlCanonicalizer
{
    private static readonly HashSet<string> TrackingParams = new(StringComparer.OrdinalIgnoreCase)
    {
        "fbclid", "gclid", "msclkid", "mc_cid", "mc_eid", "igshid", "ref", "ref_src"
    };

    /// <summary>Normalizes a URL so syntactic variants dedup to one key.
    /// Returns null for anything that is not a crawlable http(s) URL.</summary>
    public static string? Canonicalize(string url, string? baseUrl = null)
    {
        Uri? uri;
        if (baseUrl is not null && Uri.TryCreate(baseUrl, UriKind.Absolute, out var b))
            Uri.TryCreate(b, url, out uri);
        else
            Uri.TryCreate(url, UriKind.Absolute, out uri);

        if (uri is null || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return null;

        var host = uri.Host.ToLowerInvariant();
        var port = uri.IsDefaultPort ? "" : $":{uri.Port}";

        var path = uri.AbsolutePath;
        if (path.Length > 1 && path.EndsWith('/'))
            path = path.TrimEnd('/');

        var query = "";
        if (!string.IsNullOrEmpty(uri.Query))
        {
            var parsed = HttpUtility.ParseQueryString(uri.Query);
            var kept = parsed.AllKeys
                .Where(k => k is not null && !TrackingParams.Contains(k) && !k.StartsWith("utm_", StringComparison.OrdinalIgnoreCase))
                .OrderBy(k => k, StringComparer.Ordinal)
                .Select(k => $"{k}={parsed[k]}")
                .ToList();
            if (kept.Count > 0)
                query = "?" + string.Join("&", kept);
        }

        return $"{uri.Scheme}://{host}{port}{path}{query}";
    }
}
