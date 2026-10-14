using System.Linq.Expressions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace SharpMongoRepository;

/// <summary>
///     A composable, server-side query over one collection.
/// </summary>
/// <typeparam name="TDocument">Document type being queried.</typeparam>
/// <remarks>
///     <para>
///         Obtained from <see cref="IMongoRepository{TDocument}.Query" />. Every builder method returns a new
///         instance, so a partially built query is safe to keep in a field and branch from without one
///         branch's filters leaking into another.
///     </para>
///     <para>
///         This is the primary way to read when a document has no typed key. It is also the way to express
///         a read that <c>FindOneAsync</c> and <c>FilterByAsync</c> cannot: sorted, paged, hinted at a
///         particular index, or filtered on a field by name rather than through a lambda.
///     </para>
///     <para>
///         Nothing is sent to the server until a terminal method is called. Unlike
///         <see cref="IQueryable{T}" />, there is no silent fallback to filtering in memory: the whole query
///         is a single <c>find</c> command.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     var page = await repository.Query()
///         .Where(x =&gt; x.Status == "pending")
///         .Where(x =&gt; x.Total &gt; 100)
///         .OrderByDescending(x =&gt; x.CreatedAt)
///         .ToPageAsync(page: 1, pageSize: 20, cancellationToken: token);
///     </code>
/// </example>
public interface IMongoQuery<TDocument>
{
    /// <summary>
    ///     Narrows the query by a predicate.
    /// </summary>
    /// <param name="predicate">Predicate to match.</param>
    /// <returns>A new query carrying this condition.</returns>
    /// <remarks>
    ///     Repeated calls combine with <c>and</c>, so conditions can be added across several methods without
    ///     anyone having to assemble one large expression.
    /// </remarks>
    IMongoQuery<TDocument> Where(Expression<Func<TDocument, bool>> predicate);

    /// <summary>
    ///     Narrows the query by a driver filter definition.
    /// </summary>
    /// <param name="filter">Filter to apply. A JSON string converts implicitly.</param>
    /// <returns>A new query carrying this condition.</returns>
    /// <remarks>
    ///     The escape hatch for operators a lambda cannot express, such as <c>$text</c>, <c>$geoWithin</c>
    ///     or <c>$expr</c>. Combines with <c>and</c>, like <see cref="Where(Expression{Func{TDocument, bool}})" />.
    /// </remarks>
    /// <example>
    ///     <code>query.Where("{ tags: { $all: ['a', 'b'] } }")</code>
    /// </example>
    IMongoQuery<TDocument> Where(FilterDefinition<TDocument> filter);

    /// <summary>
    ///     Narrows the query by comparing a field, named as a string, to a value.
    /// </summary>
    /// <typeparam name="TValue">Type of the value compared.</typeparam>
    /// <param name="field">
    ///     Field to compare. A member name resolves through the class map, picking up
    ///     <c>[BsonElement]</c> renames and the member's own serializer; a dotted path reaches into a nested
    ///     document; a raw BSON element name such as <c>_id</c> is used as given.
    /// </param>
    /// <param name="value">Value the field must equal.</param>
    /// <returns>A new query carrying this condition.</returns>
    /// <remarks>
    ///     For documents with no compile-time shape, such as <c>BsonDocument</c>, or where the field to
    ///     filter on is decided at run time. Prefer the lambda overload when the field is known: it is
    ///     checked by the compiler and survives a rename.
    /// </remarks>
    IMongoQuery<TDocument> WhereField<TValue>(string field, TValue value);

    /// <summary>
    ///     Narrows the query to documents matching at least one of several predicates.
    /// </summary>
    /// <param name="predicates">Alternatives, combined with <c>or</c>.</param>
    /// <returns>A new query carrying this condition.</returns>
    /// <remarks>
    ///     The group as a whole is combined with <c>and</c> against anything already on the query, which is
    ///     almost always what is meant and is easy to get wrong by hand.
    /// </remarks>
    IMongoQuery<TDocument> WhereAny(params ReadOnlySpan<Expression<Func<TDocument, bool>>> predicates);

    /// <summary>
    ///     Sorts ascending by a field, replacing any sort already set.
    /// </summary>
    /// <typeparam name="TField">Type of the field sorted on.</typeparam>
    /// <param name="field">Field to sort by.</param>
    /// <returns>A new query carrying this sort.</returns>
    IMongoQuery<TDocument> OrderBy<TField>(Expression<Func<TDocument, TField>> field);

    /// <summary>
    ///     Sorts descending by a field, replacing any sort already set.
    /// </summary>
    /// <typeparam name="TField">Type of the field sorted on.</typeparam>
    /// <param name="field">Field to sort by.</param>
    /// <returns>A new query carrying this sort.</returns>
    IMongoQuery<TDocument> OrderByDescending<TField>(Expression<Func<TDocument, TField>> field);

    /// <summary>
    ///     Adds an ascending tie-breaker to the current sort.
    /// </summary>
    /// <typeparam name="TField">Type of the field sorted on.</typeparam>
    /// <param name="field">Field to sort by.</param>
    /// <returns>A new query carrying this sort.</returns>
    IMongoQuery<TDocument> ThenBy<TField>(Expression<Func<TDocument, TField>> field);

    /// <summary>
    ///     Adds a descending tie-breaker to the current sort.
    /// </summary>
    /// <typeparam name="TField">Type of the field sorted on.</typeparam>
    /// <param name="field">Field to sort by.</param>
    /// <returns>A new query carrying this sort.</returns>
    IMongoQuery<TDocument> ThenByDescending<TField>(Expression<Func<TDocument, TField>> field);

    /// <summary>
    ///     Replaces the sort with a driver sort definition.
    /// </summary>
    /// <param name="sort">Sort to apply.</param>
    /// <returns>A new query carrying this sort.</returns>
    IMongoQuery<TDocument> OrderBy(SortDefinition<TDocument> sort);

    /// <summary>
    ///     Skips the first <paramref name="count" /> matches.
    /// </summary>
    /// <param name="count">Number of documents to skip. Negative values are treated as zero.</param>
    /// <returns>A new query carrying this offset.</returns>
    /// <remarks>
    ///     The server still walks the skipped documents, so a large skip is slow however well indexed the
    ///     query is. For deep paging, sort on a unique key and filter past the last value you saw.
    /// </remarks>
    IMongoQuery<TDocument> Skip(int count);

    /// <summary>
    ///     Returns at most <paramref name="count" /> matches.
    /// </summary>
    /// <param name="count">Maximum number of documents. Negative values are treated as zero.</param>
    /// <returns>A new query carrying this limit.</returns>
    IMongoQuery<TDocument> Take(int count);

    /// <summary>
    ///     Forces the query onto a particular index.
    /// </summary>
    /// <param name="indexName">Index name, as generated by <c>MongoIndexes</c> or shown by <c>listIndexes</c>.</param>
    /// <returns>A new query carrying this hint.</returns>
    /// <remarks>
    ///     A last resort for when the planner picks the wrong index, and a liability afterwards: a hint on a
    ///     dropped or renamed index makes the query fail rather than fall back. Confirm with
    ///     <c>explain</c> before reaching for it.
    /// </remarks>
    IMongoQuery<TDocument> WithHint(string indexName);

    /// <summary>
    ///     Forces the query onto an index described by its key pattern.
    /// </summary>
    /// <param name="keyPattern">Key pattern, for example <c>{ status: 1, createdAt: -1 }</c>.</param>
    /// <returns>A new query carrying this hint.</returns>
    IMongoQuery<TDocument> WithHint(BsonDocument keyPattern);

    /// <summary>
    ///     Sets how many documents the server returns per batch.
    /// </summary>
    /// <param name="size">Documents per batch.</param>
    /// <returns>A new query carrying this batch size.</returns>
    /// <remarks>
    ///     Affects streaming reads, where it caps how much is held in memory at once, and long scans, where
    ///     a larger batch means fewer round-trips. It does not change the result.
    /// </remarks>
    IMongoQuery<TDocument> WithBatchSize(int size);

    /// <summary>
    ///     Caps how long the server spends on this query, overriding the repository's setting.
    /// </summary>
    /// <param name="maxTime">Server-side execution cap.</param>
    /// <returns>A new query carrying this cap.</returns>
    IMongoQuery<TDocument> WithMaxTime(TimeSpan maxTime);

    /// <summary>
    ///     Allows the server to use temporary disk files when the sort exceeds its memory limit.
    /// </summary>
    /// <returns>A new query allowing disk use.</returns>
    /// <remarks>
    ///     Lets a large unindexed sort succeed instead of failing, slowly. Treat needing it as a sign that
    ///     the sort wants an index.
    /// </remarks>
    IMongoQuery<TDocument> AllowDiskUse();

    /// <summary>
    ///     Enrols the query in a session, so it takes part in a transaction or shares causal consistency.
    /// </summary>
    /// <param name="session">Session to use.</param>
    /// <returns>A new query bound to that session.</returns>
    IMongoQuery<TDocument> InSession(IClientSessionHandle session);

    /// <summary>
    ///     Executes the query and buffers every match.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The matching documents.</returns>
    /// <remarks>
    ///     Add <see cref="Take" />, or use <see cref="ToAsyncEnumerable" />, when the number of matches is
    ///     not something you control.
    /// </remarks>
    Task<List<TDocument>> ToListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Executes the query and buffers a projection of every match.
    /// </summary>
    /// <typeparam name="TProjected">Shape to project into.</typeparam>
    /// <param name="projection">Projection applied server-side.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The projected results.</returns>
    /// <remarks>Only the fields named in the projection cross the wire.</remarks>
    Task<List<TProjected>> ToListAsync<TProjected>(
        Expression<Func<TDocument, TProjected>> projection,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Executes the query and streams matches a batch at a time.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the enumeration.</param>
    /// <returns>An asynchronous sequence of matching documents.</returns>
    /// <remarks>
    ///     Memory stays flat regardless of how many documents match. The cursor is released when
    ///     enumeration ends, including on an early break or a cancellation.
    /// </remarks>
    IAsyncEnumerable<TDocument> ToAsyncEnumerable(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Executes the query and streams a projection of matches a batch at a time.
    /// </summary>
    /// <typeparam name="TProjected">Shape to project into.</typeparam>
    /// <param name="projection">Projection applied server-side.</param>
    /// <param name="cancellationToken">Token to cancel the enumeration.</param>
    /// <returns>An asynchronous sequence of projected results.</returns>
    IAsyncEnumerable<TProjected> ToAsyncEnumerable<TProjected>(
        Expression<Func<TDocument, TProjected>> projection,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Returns the first match, or <see langword="null" /> when nothing matches.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The first matching document, or <see langword="null" />.</returns>
    Task<TDocument?> FirstOrDefaultAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Returns a projection of the first match, or <see langword="default" /> when nothing matches.
    /// </summary>
    /// <typeparam name="TProjected">Shape to project into.</typeparam>
    /// <param name="projection">Projection applied server-side.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The projected first match.</returns>
    Task<TProjected?> FirstOrDefaultAsync<TProjected>(
        Expression<Func<TDocument, TProjected>> projection,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Returns the only match, or <see langword="null" /> when nothing matches.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The single matching document, or <see langword="null" />.</returns>
    /// <exception cref="InvalidOperationException">Thrown when more than one document matches.</exception>
    /// <remarks>
    ///     Asks the server for two documents so it can tell one match from several. Use
    ///     <see cref="FirstOrDefaultAsync(CancellationToken)" /> when a second match is not an error.
    /// </remarks>
    Task<TDocument?> SingleOrDefaultAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Counts the matches, ignoring <see cref="Skip" /> and <see cref="Take" />.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The number of matching documents.</returns>
    Task<long> CountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Determines whether anything matches.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns><see langword="true" /> when at least one document matches.</returns>
    /// <remarks>
    ///     Stops at the first match and sends back only its <c>_id</c>, rather than counting every match.
    /// </remarks>
    Task<bool> AnyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Executes the query as one page of results.
    /// </summary>
    /// <param name="page">One-based page number. Values below 1 are treated as 1.</param>
    /// <param name="pageSize">Page size. Must be greater than zero.</param>
    /// <param name="includeTotal">
    ///     Whether to also count every match. <see langword="true" /> by default. Passing
    ///     <see langword="false" /> halves the round-trips and avoids a count that scans the whole match
    ///     set, at the cost of leaving <see cref="PagedResult{T}.TotalCount" /> at -1; the right trade for
    ///     an endless-scrolling list, which needs to know whether there is a next page but not how many
    ///     pages there are.
    /// </param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The requested page.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="pageSize" /> is not positive.</exception>
    /// <remarks>
    ///     Overrides any <see cref="Skip" /> and <see cref="Take" /> already on the query. Sort the query
    ///     first: without a sort, paging is not stable, and two pages can repeat or miss documents.
    /// </remarks>
    Task<PagedResult<TDocument>> ToPageAsync(
        int page,
        int pageSize,
        bool includeTotal = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Executes the query as one page of projected results.
    /// </summary>
    /// <typeparam name="TProjected">Shape to project into.</typeparam>
    /// <param name="page">One-based page number.</param>
    /// <param name="pageSize">Page size.</param>
    /// <param name="projection">Projection applied server-side.</param>
    /// <param name="includeTotal">Whether to also count every match.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The requested page.</returns>
    Task<PagedResult<TProjected>> ToPageAsync<TProjected>(
        int page,
        int pageSize,
        Expression<Func<TDocument, TProjected>> projection,
        bool includeTotal = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Builds the driver's fluent find for this query, without executing it.
    /// </summary>
    /// <returns>The configured fluent find.</returns>
    /// <remarks>
    ///     The escape hatch, for the handful of things the driver's fluent API offers and this one does not,
    ///     such as <c>explain</c>.
    /// </remarks>
    IFindFluent<TDocument, TDocument> ToFindFluent();

    /// <summary>
    ///     Renders the filter this query has accumulated, for logging or assertions.
    /// </summary>
    /// <returns>The filter as the server will receive it.</returns>
    BsonDocument RenderFilter();
}
