using System.Text.Json;

namespace Sumodh.Crawler.Core.Seeds;

/// <summary>Loads the bundled, user-editable list of high-fanout hub URLs (seeds/hubs.json).</summary>
public sealed class HubSeedSource(string hubsFilePath)
{
    private sealed record HubsFile(List<string>? Hubs);

    private readonly Lazy<List<string>> _hubs = new(() =>
    {
        if (!File.Exists(hubsFilePath))
            return [];
        var json = File.ReadAllText(hubsFilePath);
        var parsed = JsonSerializer.Deserialize<HubsFile>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return parsed?.Hubs ?? [];
    });

    public IReadOnlyList<string> GetHubs() => _hubs.Value;
}
