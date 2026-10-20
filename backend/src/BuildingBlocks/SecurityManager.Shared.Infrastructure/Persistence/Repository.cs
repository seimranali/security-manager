using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SecurityManager.Shared.Kernel.Abstractions;
using SecurityManager.Shared.Kernel.Entities;
using SecurityManager.Shared.Kernel.Models;

namespace SecurityManager.Shared.Infrastructure.Persistence;

/// <summary>EF Core implementation of <see cref="IRepository{T}"/>.</summary>
public class Repository<T>(DbContext context) : IRepository<T> where T : BaseEntity
{
    protected readonly DbSet<T> Set = context.Set<T>();

    public async Task<T?> GetByIdAsync(
        Guid id,
        IReadOnlyList<Expression<Func<T, object>>>? includes = null,
        CancellationToken ct = default)
    {
        if (includes is null || includes.Count == 0)
            return await Set.FindAsync([id], ct);

        return await Build(x => x.Id == id, includes).FirstOrDefaultAsync(ct);
    }

    public Task<T?> FirstOrDefaultAsync(
        Expression<Func<T, bool>> predicate,
        IReadOnlyList<Expression<Func<T, object>>>? includes = null,
        CancellationToken ct = default) =>
        Build(predicate, includes).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<T>> ListAsync(
        Expression<Func<T, bool>>? predicate = null,
        Func<IQueryable<T>, IOrderedQueryable<T>>? orderBy = null,
        int? take = null,
        IReadOnlyList<Expression<Func<T, object>>>? includes = null,
        CancellationToken ct = default)
    {
        var query = Build(predicate, includes);
        if (orderBy is not null) query = orderBy(query);
        if (take is > 0) query = query.Take(take.Value);
        return await query.ToListAsync(ct);
    }

    public async Task<PagedResult<T>> GetPagedAsync(
        int page,
        int pageSize,
        Expression<Func<T, bool>>? predicate = null,
        Func<IQueryable<T>, IOrderedQueryable<T>>? orderBy = null,
        IReadOnlyList<Expression<Func<T, object>>>? includes = null,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, PagedQuery.MaxPageSize);

        var query = Build(predicate, includes);
        var total = await query.CountAsync(ct);

        query = orderBy is not null ? orderBy(query) : query.OrderByDescending(x => x.CreatedAt);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).AsNoTracking().ToListAsync(ct);

        return new PagedResult<T>(items, page, pageSize, total);
    }

    public async Task<PagedResult<ValueCount<TValue>>> GetValueCountsPagedAsync<TValue>(
        Expression<Func<T, TValue>> selector,
        int page,
        int pageSize,
        Expression<Func<T, bool>>? predicate = null,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        IQueryable<T> source = predicate is null ? Set : Set.Where(predicate);
        var grouped = source.GroupBy(selector).Select(g => new { g.Key, Count = g.Count() });

        var total = await grouped.CountAsync(ct);
        var rows = await grouped
            .OrderByDescending(x => x.Count).ThenBy(x => x.Key)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<ValueCount<TValue>>(rows.Select(r => new ValueCount<TValue>(r.Key, r.Count)).ToList(), page, pageSize, total);
    }

    public Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken ct = default) =>
        predicate is null ? Set.CountAsync(ct) : Set.CountAsync(predicate, ct);

    public Task<bool> AnyAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default) =>
        Set.AnyAsync(predicate, ct);

    public async Task AddAsync(T entity, CancellationToken ct = default) => await Set.AddAsync(entity, ct);

    public Task AddRangeAsync(IEnumerable<T> entities, CancellationToken ct = default) => Set.AddRangeAsync(entities, ct);

    public void Update(T entity) => Set.Update(entity);

    public void Remove(T entity) => Set.Remove(entity);

    public void RemoveRange(IEnumerable<T> entities) => Set.RemoveRange(entities);

    private IQueryable<T> Build(
        Expression<Func<T, bool>>? predicate,
        IReadOnlyList<Expression<Func<T, object>>>? includes)
    {
        IQueryable<T> query = Set;
        if (includes is not null)
        {
            foreach (var include in includes) query = query.Include(include);
        }
        return predicate is null ? query : query.Where(predicate);
    }
}
