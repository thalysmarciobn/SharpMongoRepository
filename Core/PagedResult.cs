namespace SharpMongoRepository;

/// <summary>
///     One page of results plus the total number of matches.
/// </summary>
/// <typeparam name="T">Item type.</typeparam>
/// <param name="Items">Items on this page.</param>
/// <param name="TotalCount">Total number of documents matching the filter, across all pages.</param>
/// <param name="Page">One-based index of this page.</param>
/// <param name="PageSize">Maximum number of items a page holds.</param>
/// <remarks>
///     Paging is applied server-side with <c>skip</c> and <c>limit</c>, so only one page crosses the wire.
///     Be aware that <c>skip</c> costs more the deeper you page, because the server still walks the skipped
///     documents; for deep or unbounded paging, filter on the last key you saw instead.
/// </remarks>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, long TotalCount, int Page, int PageSize)
{
    /// <summary>
    ///     Gets the total number of pages, given <see cref="TotalCount" /> and <see cref="PageSize" />.
    /// </summary>
    public int TotalPages => PageSize <= 0 ? 0 : (int)((TotalCount + PageSize - 1) / PageSize);

    /// <summary>
    ///     Gets a value indicating whether another page follows this one.
    /// </summary>
    public bool HasNextPage => Page < TotalPages;

    /// <summary>
    ///     Gets a value indicating whether a page precedes this one.
    /// </summary>
    public bool HasPreviousPage => Page > 1;
}
