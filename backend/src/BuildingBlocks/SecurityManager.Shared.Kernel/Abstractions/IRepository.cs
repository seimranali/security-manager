using System.Linq.Expressions;
using SecurityManager.Shared.Kernel.Entities;
using SecurityManager.Shared.Kernel.Models;

namespace SecurityManager.Shared.Kernel.Abstractions;

/// <summary>
/// Persistence-agnostic generic repository. The application layer depends only on this
/// abstraction; the EF Core implementation lives in the infrastructure layer.
/// </summary>
public interface IRepository<T> where T : BaseEntity
{
    Task<T?> GetByIdAsync(
        Guid id,
        IReadOnlyList<Expression<Func<T, object>>>? includes = null,
        CancellationToken ct = default);

    Task<T?> FirstOrDefaultAsync(
        Expression<Func<T, bool>> predicate,
        IReadOnlyList<Expression<Func<T, object>>>? includes = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<T>> ListAsync(
        Expression<Func<T, bool>>? predicate = null,
        Func<IQueryable<T>, IOrderedQueryable<T>>? orderBy = null,
        int? take = null,
        IReadOnlyList<Expression<Func<T, object>>>? includes = null,
        CancellationToken ct = default);

    Task<PagedResult<T>> GetPagedAsync(
        int page,
        int pageSize,
        Expression<Func<T, bool>>? predicate = null,
        Func<IQueryable<T>, IOrderedQueryable<T>>? orderBy = null,
        IReadOnlyList<Expression<Func<T, object>>>? includes = null,
        CancellationToken ct = default);

    /// <summary>Distinct values of <paramref name="selector"/> with occurrence counts, most frequent first (for lookups).</summary>
    Task<PagedResult<ValueCount<TValue>>> GetValueCountsPagedAsync<TValue>(
        Expression<Func<T, TValue>> selector,
        int page,
        int pageSize,
        Expression<Func<T, bool>>? predicate = null,
        CancellationToken ct = default);

    Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken ct = default);

    Task<bool> AnyAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default);

    Task AddAsync(T entity, CancellationToken ct = default);

    Task AddRangeAsync(IEnumerable<T> entities, CancellationToken ct = default);

    void Update(T entity);

    void Remove(T entity);

    void RemoveRange(IEnumerable<T> entities);
}
