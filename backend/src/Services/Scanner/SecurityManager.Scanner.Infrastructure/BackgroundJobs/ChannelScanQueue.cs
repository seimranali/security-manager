using System.Threading.Channels;
using SecurityManager.Scanner.Application.Abstractions;

namespace SecurityManager.Scanner.Infrastructure.BackgroundJobs;

/// <summary>In-process job queue. Job state is persisted, so the queue is rebuilt on restart.</summary>
public sealed class ChannelScanQueue : IScanQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions
    {
        SingleReader = false,
        SingleWriter = false
    });

    public ValueTask EnqueueAsync(Guid jobId, CancellationToken ct = default) => _channel.Writer.WriteAsync(jobId, ct);

    public IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
}
