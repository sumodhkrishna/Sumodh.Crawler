using System.Threading.Channels;

namespace Sumodh.Crawler.Core.Orchestration;

/// <summary>In-process handoff from the API to the orchestrator.</summary>
public sealed class JobQueue
{
    private readonly Channel<string> _channel = Channel.CreateUnbounded<string>();

    public void Enqueue(string jobId) => _channel.Writer.TryWrite(jobId);

    public IAsyncEnumerable<string> ReadAllAsync(CancellationToken ct) =>
        _channel.Reader.ReadAllAsync(ct);
}
