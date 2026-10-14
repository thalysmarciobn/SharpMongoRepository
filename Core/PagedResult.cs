namespace SharpMongoRepository;

/// <summary>
///     One page of results, and the total number of matches when it was asked for.
/// </summary>
/// <typeparam name="T">Item type.</typeparam>
/// <param name="Items">Items on this page.</param>
/// <param name="TotalCount">
///     Total number of documents matching the filter across all pages, or
///     <see cref="TotalCountNotCounted" /> when the count was skipped.
/// </param>
/// <param name="Page">One-based index of this page.</param>
/// <param name="PageSize">Maximum number of items a page holds.</param>
/// <remarks>
///     Paging is applied server-side with <c>skip</c> and <c>limit</c>, so only one page crosses the wire.
///     Be aware that <c>skip</c> costs more the deeper you page, because the server still walks the skipped
///     documents; for deep or unbounded paging, sort on a unique key and filter past the last value you saw.
/// </remarks>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, long TotalCount, int Page, int PageSize)
{
    /// <summary>
    ///     The value <see cref="TotalCount" /> carries when counting was skipped.
    /// </summary>
    /// <remarks>
    ///     Deliberately not 0, so an uncounted page cannot be mistaken for an empty result set.
    /// </remarks>
    public const long TotalCountNotCounted = -1;

    /// <summary>
    ///     Gets a value indicating whether <see cref="TotalCount" /> holds a real count.
    /// </summary>
    public bool HasTotalCount => TotalCount >= 0;

    /// <summary>
    ///     Gets the total number of pages, or 0 when the total was not counted.
    /// </summary>
    public int TotalPages => !HasTotalCount || PageSize <= 0
        ? 0
        : (int)((TotalCount + PageSize - 1) / PageSize);

    /// <summary>
    ///     Gets a value indicating whether another page follows this one.
    /// </summary>
    /// <remarks>
    ///     When the total was not counted, this is inferred from the page being full, which is the usual
    ///     signal an endless-scrolling list needs. A full last page therefore reports one more page that
    ///     turns out to be empty.
    /// </remarks>
    public bool HasNextPage => HasTotalCount ? Page < TotalPages : Items.Count == PageSize;

    /// <summary>
    ///     Gets a value indicating whether a page precedes this one.
    /// </summary>
    public bool HasPreviousPage => Page > 1;
}
