using System.Collections.Concurrent;

namespace Sumodh.Crawler.Core.Fetching;

/// <summary>Serializes requests per host and enforces a minimum delay between them,
/// so aggregate concurrency stays high without hammering any single site.</summary>
public sealed class DomainRateLimiter
{
    private sealed class HostGate
    {
        public readonly SemaphoreSlim Semaphore = new(1, 1);
        public DateTime NextAllowedUtc = DateTime.MinValue;
    }

    private readonly ConcurrentDictionary<string, HostGate> _gates = new(StringComparer.OrdinalIgnoreCase);

    public async Task WaitAsync(string host, TimeSpan minDelay, CancellationToken ct = default)
    {
        var gate = _gates.GetOrAdd(host, _ => new HostGate());
        await gate.Semaphore.WaitAsync(ct);
        try
        {
            var wait = gate.NextAllowedUtc - DateTime.UtcNow;
            if (wait > TimeSpan.Zero)
                await Task.Delay(wait, ct);
            gate.NextAllowedUtc = DateTime.UtcNow + minDelay;
        }
        finally
        {
            gate.Semaphore.Release();
        }
    }
}
