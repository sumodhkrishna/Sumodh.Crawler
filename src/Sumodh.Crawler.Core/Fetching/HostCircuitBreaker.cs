using System.Collections.Concurrent;

namespace Sumodh.Crawler.Core.Fetching;

/// <summary>Per-host circuit breaker: after N consecutive failures the host is skipped
/// for a cooldown window, so one dead site can't stall the worker pool.</summary>
public sealed class HostCircuitBreaker(int failureThreshold = 5, int cooldownSeconds = 30)
{
    private sealed class State
    {
        public int ConsecutiveFailures;
        public DateTime OpenUntilUtc = DateTime.MinValue;
    }

    private readonly ConcurrentDictionary<string, State> _hosts = new(StringComparer.OrdinalIgnoreCase);

    public bool IsOpen(string host) =>
        _hosts.TryGetValue(host, out var s) && DateTime.UtcNow < s.OpenUntilUtc;

    public void RecordSuccess(string host)
    {
        if (_hosts.TryGetValue(host, out var s))
            lock (s) { s.ConsecutiveFailures = 0; }
    }

    public void RecordFailure(string host)
    {
        var s = _hosts.GetOrAdd(host, _ => new State());
        lock (s)
        {
            if (++s.ConsecutiveFailures >= failureThreshold)
            {
                s.OpenUntilUtc = DateTime.UtcNow.AddSeconds(cooldownSeconds);
                s.ConsecutiveFailures = 0; // half-open after cooldown: next attempt decides
            }
        }
    }
}
