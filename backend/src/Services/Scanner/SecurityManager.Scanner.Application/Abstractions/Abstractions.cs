using SecurityManager.Scanner.Domain.Entities;
using SecurityManager.Scanner.Domain.Models;
using SecurityManager.Shared.Kernel.Abstractions;
using SecurityManager.Shared.Kernel.Contracts;

namespace SecurityManager.Scanner.Application.Abstractions;

public interface IScannerUnitOfWork : IUnitOfWork
{
    IRepository<ScanTarget> Targets { get; }
    IRepository<ScanSchedule> Schedules { get; }
    IRepository<ScanJob> Jobs { get; }
    IRepository<ScanFinding> Findings { get; }
}

public interface IPortScanner
{
    Task<IReadOnlyList<PortProbeResult>> ScanAsync(string host, IReadOnlyList<int> ports, CancellationToken ct = default);
}

public interface IHttpProbe
{
    Task<HttpProbeResult> ProbeAsync(string url, CancellationToken ct = default);
}

public interface ITlsProbe
{
    Task<TlsProbeResult> ProbeAsync(string host, int port, CancellationToken ct = default);
}

/// <summary>Queue of scan job ids awaiting execution by the background worker.</summary>
public interface IScanQueue
{
    ValueTask EnqueueAsync(Guid jobId, CancellationToken ct = default);
    IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken ct);
}

public interface IAlertPublisher
{
    Task PublishAsync(AlertMessage message, CancellationToken ct = default);
}
