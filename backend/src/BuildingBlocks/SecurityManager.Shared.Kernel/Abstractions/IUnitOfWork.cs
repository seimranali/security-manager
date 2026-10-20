using SecurityManager.Shared.Kernel.Entities;

namespace SecurityManager.Shared.Kernel.Abstractions;

/// <summary>
/// Coordinates the work of multiple repositories and commits it as a single atomic operation.
/// </summary>
public interface IUnitOfWork : IDisposable
{
    IRepository<T> Repository<T>() where T : BaseEntity;

    Task<int> SaveChangesAsync(CancellationToken ct = default);

    Task BeginTransactionAsync(CancellationToken ct = default);

    Task CommitTransactionAsync(CancellationToken ct = default);

    Task RollbackTransactionAsync(CancellationToken ct = default);
}
