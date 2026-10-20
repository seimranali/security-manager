namespace SecurityManager.Shared.Kernel.Models;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public PagedResult<TOut> Map<TOut>(Func<T, TOut> map) =>
        new(Items.Select(map).ToList(), Page, PageSize, TotalCount);
}

/// <summary>A distinct value and how many records contain it.</summary>
public sealed record ValueCount<TValue>(TValue Value, int Count);

/// <summary>Paged, searchable lookup of distinct values (used by async dropdowns).</summary>
public sealed class LookupQuery : PagedQuery
{
    public string? Search { get; set; }
}

public abstract class PagedQuery
{
    /// <summary>Upper bound so grids can load a working set for client-side sort/group/pivot.</summary>
    public const int MaxPageSize = 1000;

    private int _page = 1;
    private int _pageSize = 20;

    public int Page
    {
        get => _page;
        set => _page = value < 1 ? 1 : value;
    }

    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = Math.Clamp(value, 1, MaxPageSize);
    }
}
