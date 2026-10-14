using System.Linq.Expressions;
using MongoDB.Driver;

namespace SharpMongoRepository;

/// <summary>
///     Repository over a single MongoDB collection.
/// </summary>
/// <typeparam name="TDocument">Document type, which must implement <see cref="IDocument{TKey}" />.</typeparam>
/// <typeparam name="TKey">Type of the document's primary key.</typeparam>
/// <remarks>
///     <para>
///         Every asynchronous method takes a <see cref="CancellationToken" />, so a cancelled request stops
///         waiting on the database instead of holding a connection to the end.
///     </para>
///     <para>
///         Every method takes an optional <see cref="IClientSessionHandle" />. Pass the session handed to you
///         by <see cref="WithTransactionAsync{TResult}" /> to enrol an operation in that transaction; leave it
///         <see langword="null" /> outside a transaction.
///     </para>
///     <para>
///         A small synchronous surface is kept for callers that need it, but prefer the asynchronous methods:
///         a synchronous database call blocks a thread pool thread for the whole network round-trip.
///     </para>
/// </remarks>
public interface IMongoRepository<TDocument, TKey>
    where TDocument : IDocument<TKey>
    where TKey : notnull
{
    /// <summary>
    ///     Gets the underlying driver collection.
    /// </summary>
    /// <remarks>
    ///     The escape hatch for anything this interface does not cover: aggregation pipelines, change
    ///     streams, <c>$geoNear</c>, per-call read preferences. Using it is not a defeat; wrapping the entire
    ///     driver would be.
    /// </remarks>
    IMongoCollection<TDocument> Collection { get; }

    /// <summary>
    ///     Starts a fluent find over the given filter.
    /// </summary>
    /// <param name="filter">Filter definition.</param>
    /// <param name="session">Session to enrol the read in, or <see langword="null" />.</param>
    /// <returns>A fluent find that further sorts, projects, skips or limits before executing.</returns>
    IFindFluent<TDocument, TDocument> Find(
        FilterDefinition<TDocument> filter,
        IClientSessionHandle? session = null);

    /// <summary>
    ///     Exposes the collection as an <see cref="IQueryable{T}" /> translated to an aggregation pipeline.
    /// </summary>
    /// <param name="session">Session to enrol the read in, or <see langword="null" />.</param>
    /// <returns>A queryable over the collection.</returns>
    /// <remarks>
    ///     Not every LINQ operator has a server-side translation. An untranslatable expression throws rather
    ///     than silently fetching the collection and filtering in memory.
    /// </remarks>
    IQueryable<TDocument> AsQueryable(IClientSessionHandle? session = null);

    /// <summary>
    ///     Finds a document by its primary key.
    /// </summary>
    /// <param name="id">The key to look up.</param>
    /// <param name="session">Session to enrol the read in, or <see langword="null" />.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The document, or <see langword="null" /> when no document has that key.</returns>
    Task<TDocument?> FindByIdAsync(
        TKey id,
        IClientSessionHandle? session = null,
        CancellationToken cancellationToken = default);

    /// <inheritdoc cref="FindByIdAsync" />
    TDocument? FindById(TKey id, IClientSessionHandle? session = null);

    /// <summary>
    ///     Finds the first document matching a predicate.
    /// </summary>
    /// <param name="filter">Predicate to match.</param>
    /// <param name="session">Session to enrol the read in, or <see langword="null" />.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The first matching document, or <see langword="null" /> when nothing matches.</returns>
    Task<TDocument?> FindOneAsync(
        Expression<Func<TDocument, bool>> filter,
        IClientSessionHandle? session = null,
        CancellationToken cancellationToken = default);

    /// <inheritdoc cref="FindOneAsync" />
    TDocument? FindOne(Expression<Func<TDocument, bool>> filter, IClientSessionHandle? session = null);

    /// <summary>
    ///     Returns every document matching a predicate.
    /// </summary>
    /// <param name="filter">Predicate to match.</param>
    /// <param name="session">Session to enrol the read in, or <see langword="null" />.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The matching documents.</returns>
    /// <remarks>
    ///     The whole result set is buffered into the returned list. For a result set whose size you do not
    ///     control, use <see cref="StreamAsync(Expression{Func{TDocument, bool}}?, IClientSessionHandle?, CancellationToken)" />
    ///     or <see cref="GetPageAsync" /> instead.
    /// </remarks>
    Task<List<TDocument>> FilterByAsync(
        Expression<Func<TDocument, bool>> filter,
        IClientSessionHandle? session = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Returns a projection of every document matching a predicate.
    /// </summary>
    /// <typeparam name="TProjected">Shape to project into.</typeparam>
    /// <param name="filter">Predicate to match.</param>
    /// <param name="projection">Projection applied server-side.</param>
    /// <param name="session">Session to enrol the read in, or <see langword="null" />.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The projected results.</returns>
    /// <remarks>
    ///     The projection runs on the server, so only the fields you ask for cross the wire. This is the
    ///     cheapest read in the interface and worth reaching for whenever you do not need whole documents.
    /// </remarks>
    Task<List<TProjected>> FilterByAsync<TProjected>(
        Expression<Func<TDocument, bool>> filter,
        Expression<Func<TDocument, TProjected>> projection,
        IClientSessionHandle? session = null,
        CancellationToken cancellationToken = default);

    /// <inheritdoc cref="FilterByAsync(Expression{Func{TDocument, bool}}, IClientSessionHandle?, CancellationToken)" />
    IEnumerable<TDocument> FilterBy(
        Expression<Func<TDocument, bool>> filter,
        IClientSessionHandle? session = null);

    /// <inheritdoc cref="FilterByAsync{TProjected}(Expression{Func{TDocument, bool}}, Expression{Func{TDocument, TProjected}}, IClientSessionHandle?, CancellationToken)" />
    IEnumerable<TProjected> FilterBy<TProjected>(
        Expression<Func<TDocument, bool>> filter,
        Expression<Func<TDocument, TProjected>> projection,
        IClientSessionHandle? session = null);

    /// <summary>
    ///     Streams documents matching a predicate, a batch at a time.
    /// </summary>
    /// <param name="filter">Predicate to match, or <see langword="null" /> for the whole collection.</param>
    /// <param name="session">Session to enrol the read in, or <see langword="null" />.</param>
    /// <param name="cancellationToken">Token to cancel the enumeration.</param>
    /// <returns>An asynchronous sequence of matching documents.</returns>
    /// <remarks>
    ///     Memory stays flat regardless of how many documents match, because only the current server batch is
    ///     held. The cursor is disposed when enumeration ends, including when you break out early or the token
    ///     is cancelled, which the raw <see cref="IAsyncCursor{TDocument}" /> this replaced did not guarantee.
    /// </remarks>
    IAsyncEnumerable<TDocument> StreamAsync(
        Expression<Func<TDocument, bool>>? filter = null,
        IClientSessionHandle? session = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Streams a projection of documents matching a predicate, a batch at a time.
    /// </summary>
    /// <typeparam name="TProjected">Shape to project into.</typeparam>
    /// <param name="filter">Predicate to match, or <see langword="null" /> for the whole collection.</param>
    /// <param name="projection">Projection applied server-side.</param>
    /// <param name="session">Session to enrol the read in, or <see langword="null" />.</param>
    /// <param name="cancellationToken">Token to cancel the enumeration.</param>
    /// <returns>An asynchronous sequence of projected results.</returns>
    IAsyncEnumerable<TProjected> StreamAsync<TProjected>(
        Expression<Func<TDocument, bool>>? filter,
        Expression<Func<TDocument, TProjected>> projection,
        IClientSessionHandle? session = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Returns one page of documents together with the total match count.
    /// </summary>
    /// <param name="page">One-based page number. Values below 1 are treated as 1.</param>
    /// <param name="pageSize">Page size. Must be greater than zero.</param>
    /// <param name="filter">Predicate to match, or <see langword="null" /> for the whole collection.</param>
    /// <param name="sort">Sort order. Strongly recommended: without one, paging is not stable.</param>
    /// <param name="session">Session to enrol the read in, or <see langword="null" />.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The requested page.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="pageSize" /> is not positive.</exception>
    Task<PagedResult<TDocument>> GetPageAsync(
        int page,
        int pageSize,
        Expression<Func<TDocument, bool>>? filter = null,
        SortDefinition<TDocument>? sort = null,
        IClientSessionHandle? session = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Determines whether any document matches a predicate.
    /// </summary>
    /// <param name="filter">Predicate to match.</param>
    /// <param name="session">Session to enrol the read in, or <see langword="null" />.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns><see langword="true" /> when at least one document matches.</returns>
    /// <remarks>
    ///     Implemented as a find limited to one document and projected down to <c>_id</c>, so the server stops
    ///     at the first hit. Counting and comparing to zero would walk every match.
    /// </remarks>
    Task<bool> ExistsAsync(
        Expression<Func<TDocument, bool>> filter,
        IClientSessionHandle? session = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Counts documents matching a predicate.
    /// </summary>
    /// <param name="filter">Predicate to match, or <see langword="null" /> to count the whole collection.</param>
    /// <param name="session">Session to enrol the read in, or <see langword="null" />.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The number of matching documents.</returns>
    /// <remarks>
    ///     An exact count scans the index or the collection. To count a whole collection cheaply and accept a
    ///     possibly stale answer, use <see cref="EstimateCountAsync" />.
    /// </remarks>
    Task<long> CountAsync(
        Expression<Func<TDocument, bool>>? filter = null,
        IClientSessionHandle? session = null,
        CancellationToken cancellationToken = default);

    /// <inheritdoc cref="CountAsync" />
    long Count(Expression<Func<TDocument, bool>>? filter = null, IClientSessionHandle? session = null);

    /// <summary>
    ///     Reads the collection's document count from collection metadata.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The estimated number of documents.</returns>
    /// <remarks>
    ///     Constant time, because it does not look at documents. The figure can lag reality after an unclean
    ///     shutdown, and it cannot be filtered or used inside a transaction.
    /// </remarks>
    Task<long> EstimateCountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Returns the distinct values of one field across documents matching a predicate.
    /// </summary>
    /// <typeparam name="TField">Type of the field.</typeparam>
    /// <param name="field">Selector for the field.</param>
    /// <param name="filter">Predicate to match, or <see langword="null" /> for the whole collection.</param>
    /// <param name="session">Session to enrol the read in, or <see langword="null" />.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The distinct values.</returns>
    Task<List<TField>> DistinctAsync<TField>(
        Expression<Func<TDocument, TField>> field,
        Expression<Func<TDocument, bool>>? filter = null,
        IClientSessionHandle? session = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Inserts one document, generating its key when it is empty.
    /// </summary>
    /// <param name="document">Document to insert. Its <see cref="IDocument{TKey}.Id" /> is written back when generated.</param>
    /// <param name="session">Session to enrol the write in, or <see langword="null" />.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task that completes when the server has acknowledged the write.</returns>
    Task InsertOneAsync(
        TDocument document,
        IClientSessionHandle? session = null,
        CancellationToken cancellationToken = default);

    /// <inheritdoc cref="InsertOneAsync" />
    void InsertOne(TDocument document, IClientSessionHandle? session = null);

    /// <summary>
    ///     Inserts several documents in one command, generating keys where they are empty.
    /// </summary>
    /// <param name="documents">Documents to insert.</param>
    /// <param name="ordered">
    ///     When <see langword="true" />, the default, the server stops at the first failure. Pass
    ///     <see langword="false" /> to attempt every document and have failures reported together, which is
    ///     also faster because the server may parallelise the batch.
    /// </param>
    /// <param name="session">Session to enrol the write in, or <see langword="null" />.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task that completes when the server has acknowledged the write.</returns>
    Task InsertManyAsync(
        IEnumerable<TDocument> documents,
        bool ordered = true,
        IClientSessionHandle? session = null,
        CancellationToken cancellationToken = default);

    /// <inheritdoc cref="InsertManyAsync" />
    void InsertMany(IEnumerable<TDocument> documents, bool ordered = true, IClientSessionHandle? session = null);

    /// <summary>
    ///     Replaces the document with the same key as <paramref name="document" />.
    /// </summary>
    /// <param name="document">Replacement document; its key selects the target.</param>
    /// <param name="upsert">When <see langword="true" />, insert the document if no match exists.</param>
    /// <param name="session">Session to enrol the write in, or <see langword="null" />.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The result, carrying matched and modified counts.</returns>
    /// <remarks>
    ///     This overwrites every field. To change a few fields without sending the rest, and without losing a
    ///     concurrent writer's changes to other fields, use <see cref="UpdateByIdAsync" />.
    /// </remarks>
    Task<ReplaceOneResult> ReplaceOneAsync(
        TDocument document,
        bool upsert = false,
        IClientSessionHandle? session = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Replaces the document with the same key and returns one version of it.
    /// </summary>
    /// <param name="document">Replacement document; its key selects the target.</param>
    /// <param name="returnDocument">Whether to return the document before or after replacement.</param>
    /// <param name="upsert">When <see langword="true" />, insert the document if no match exists.</param>
    /// <param name="session">Session to enrol the write in, or <see langword="null" />.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The requested version of the document, or <see langword="null" /> when nothing matched.</returns>
    Task<TDocument?> FindOneAndReplaceAsync(
        TDocument document,
        ReturnDocument returnDocument = ReturnDocument.After,
        bool upsert = false,
        IClientSessionHandle? session = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Applies a partial update to the document with the given key.
    /// </summary>
    /// <param name="id">Key of the document to update.</param>
    /// <param name="update">Update definition, built with <c>Builders&lt;TDocument&gt;.Update</c>.</param>
    /// <param name="upsert">When <see langword="true" />, create the document if it does not exist.</param>
    /// <param name="session">Session to enrol the write in, or <see langword="null" />.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The result, carrying matched and modified counts.</returns>
    /// <example>
    ///     <code>
    ///     await repo.UpdateByIdAsync(id, Builders&lt;Order&gt;.Update
    ///         .Set(x =&gt; x.Status, OrderStatus.Shipped)
    ///         .Inc(x =&gt; x.Revision, 1));
    ///     </code>
    /// </example>
    Task<UpdateResult> UpdateByIdAsync(
        TKey id,
        UpdateDefinition<TDocument> update,
        bool upsert = false,
        IClientSessionHandle? session = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Applies a partial update to the first document matching a predicate.
    /// </summary>
    /// <param name="filter">Predicate selecting the document.</param>
    /// <param name="update">Update definition.</param>
    /// <param name="upsert">When <see langword="true" />, create the document if nothing matches.</param>
    /// <param name="session">Session to enrol the write in, or <see langword="null" />.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The result, carrying matched and modified counts.</returns>
    Task<UpdateResult> UpdateOneAsync(
        Expression<Func<TDocument, bool>> filter,
        UpdateDefinition<TDocument> update,
        bool upsert = false,
        IClientSessionHandle? session = null,
        CancellationToken cancellationToken = default);

    /// <inheritdoc cref="UpdateOneAsync" />
    UpdateResult UpdateOne(
        Expression<Func<TDocument, bool>> filter,
        UpdateDefinition<TDocument> update,
        bool upsert = false,
        IClientSessionHandle? session = null);

    /// <summary>
    ///     Applies a partial update to every document matching a predicate.
    /// </summary>
    /// <param name="filter">Predicate selecting the documents.</param>
    /// <param name="update">Update definition.</param>
    /// <param name="session">Session to enrol the write in, or <see langword="null" />.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The result, carrying matched and modified counts.</returns>
    /// <remarks>
    ///     One command regardless of how many documents match: the documents are never fetched into the
    ///     client. This is the right way to apply the same change in bulk.
    /// </remarks>
    Task<UpdateResult> UpdateManyAsync(
        Expression<Func<TDocument, bool>> filter,
        UpdateDefinition<TDocument> update,
        IClientSessionHandle? session = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Applies a partial update to the first matching document and returns one version of it.
    /// </summary>
    /// <param name="filter">Predicate selecting the document.</param>
    /// <param name="update">Update definition.</param>
    /// <param name="returnDocument">Whether to return the document before or after the update.</param>
    /// <param name="upsert">When <see langword="true" />, create the document if nothing matches.</param>
    /// <param name="session">Session to enrol the write in, or <see langword="null" />.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The requested version of the document, or <see langword="null" /> when nothing matched.</returns>
    /// <remarks>
    ///     Atomic read-modify-write in a single command, which is how you implement a counter, a queue claim
    ///     or an optimistic-concurrency check without a transaction.
    /// </remarks>
    Task<TDocument?> FindOneAndUpdateAsync(
        Expression<Func<TDocument, bool>> filter,
        UpdateDefinition<TDocument> update,
        ReturnDocument returnDocument = ReturnDocument.After,
        bool upsert = false,
        IClientSessionHandle? session = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Deletes the document with the given key.
    /// </summary>
    /// <param name="id">Key of the document to delete.</param>
    /// <param name="session">Session to enrol the write in, or <see langword="null" />.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The number of documents deleted: 0 or 1.</returns>
    /// <remarks>
    ///     Returns a count so the caller can tell a successful delete from a no-op. The previous version
    ///     returned <see langword="void" /> and was implemented with <c>findOneAndDelete</c>, which fetched
    ///     the whole document over the wire only to discard it.
    /// </remarks>
    Task<long> DeleteByIdAsync(
        TKey id,
        IClientSessionHandle? session = null,
        CancellationToken cancellationToken = default);

    /// <inheritdoc cref="DeleteByIdAsync" />
    long DeleteById(TKey id, IClientSessionHandle? session = null);

    /// <summary>
    ///     Deletes the first document matching a predicate.
    /// </summary>
    /// <param name="filter">Predicate selecting the document.</param>
    /// <param name="session">Session to enrol the write in, or <see langword="null" />.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The number of documents deleted: 0 or 1.</returns>
    Task<long> DeleteOneAsync(
        Expression<Func<TDocument, bool>> filter,
        IClientSessionHandle? session = null,
        CancellationToken cancellationToken = default);

    /// <inheritdoc cref="DeleteOneAsync" />
    long DeleteOne(Expression<Func<TDocument, bool>> filter, IClientSessionHandle? session = null);

    /// <summary>
    ///     Deletes every document matching a predicate.
    /// </summary>
    /// <param name="filter">Predicate selecting the documents.</param>
    /// <param name="session">Session to enrol the write in, or <see langword="null" />.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The number of documents deleted.</returns>
    Task<long> DeleteManyAsync(
        Expression<Func<TDocument, bool>> filter,
        IClientSessionHandle? session = null,
        CancellationToken cancellationToken = default);

    /// <inheritdoc cref="DeleteManyAsync" />
    long DeleteMany(Expression<Func<TDocument, bool>> filter, IClientSessionHandle? session = null);

    /// <summary>
    ///     Deletes the first document matching a predicate and returns it.
    /// </summary>
    /// <param name="filter">Predicate selecting the document.</param>
    /// <param name="session">Session to enrol the write in, or <see langword="null" />.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The deleted document, or <see langword="null" /> when nothing matched.</returns>
    /// <remarks>
    ///     Use this only when you need the deleted document, for instance to publish an event. When you do
    ///     not, <see cref="DeleteOneAsync" /> avoids transferring it.
    /// </remarks>
    Task<TDocument?> FindOneAndDeleteAsync(
        Expression<Func<TDocument, bool>> filter,
        IClientSessionHandle? session = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Sends a batch of mixed writes in a single command.
    /// </summary>
    /// <param name="requests">Write models: inserts, updates, replacements, deletions, in any mix.</param>
    /// <param name="ordered">
    ///     When <see langword="true" />, the default, the server applies the batch in order and stops at the
    ///     first failure. Pass <see langword="false" /> for maximum throughput.
    /// </param>
    /// <param name="session">Session to enrol the writes in, or <see langword="null" />.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The result, carrying per-operation counts.</returns>
    /// <remarks>
    ///     One round-trip for the whole batch. Writing a thousand documents in a loop costs a thousand
    ///     round-trips; the same work here costs one.
    /// </remarks>
    Task<BulkWriteResult<TDocument>> BulkWriteAsync(
        IEnumerable<WriteModel<TDocument>> requests,
        bool ordered = true,
        IClientSessionHandle? session = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Runs a unit of work inside a transaction, retrying where the server says retrying is safe.
    /// </summary>
    /// <typeparam name="TResult">Type the unit of work returns.</typeparam>
    /// <param name="body">
    ///     The unit of work. It receives the session, which it must pass to every repository call that should
    ///     take part in the transaction. It may run more than once, so it must be idempotent in its
    ///     non-database side effects.
    /// </param>
    /// <param name="transactionOptions">Read concern, write concern and read preference for the transaction.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Whatever <paramref name="body" /> returned.</returns>
    /// <remarks>
    ///     <para>
    ///         Delegates to the driver's own transaction runner, which retries the body on
    ///         <c>TransientTransactionError</c> and retries the commit on <c>UnknownTransactionCommitResult</c>.
    ///         The previous version committed once and aborted on any exception, so a transaction lost to a
    ///         primary step-down surfaced as a hard failure instead of being retried.
    ///     </para>
    ///     <para>
    ///         Transactions require a replica set or a sharded cluster; they are not available on a
    ///         standalone server.
    ///     </para>
    ///     <para>
    ///         To span several collections, start a session with <see cref="StartSessionAsync" /> and pass it
    ///         to each repository. All repositories registered against the same connection string share one
    ///         <see cref="IMongoClient" />, which is what makes that possible.
    ///     </para>
    /// </remarks>
    Task<TResult> WithTransactionAsync<TResult>(
        Func<IClientSessionHandle, CancellationToken, Task<TResult>> body,
        TransactionOptions? transactionOptions = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Runs a unit of work that returns no value inside a transaction.
    /// </summary>
    /// <param name="body">The unit of work. See the generic overload for the retry contract.</param>
    /// <param name="transactionOptions">Read concern, write concern and read preference for the transaction.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task that completes when the transaction has committed.</returns>
    Task WithTransactionAsync(
        Func<IClientSessionHandle, CancellationToken, Task> body,
        TransactionOptions? transactionOptions = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Starts a session, for sharing causal consistency or a transaction across repositories.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The session. The caller owns it and must dispose it.</returns>
    Task<IClientSessionHandle> StartSessionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Creates any configured index that does not yet exist.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task that completes once the indexes are in place.</returns>
    /// <remarks>
    ///     Called for you: at start-up under <see cref="IndexCreationMode.Startup" />, or before the first
    ///     operation under <see cref="IndexCreationMode.FirstUse" />. It is idempotent and cheap to call
    ///     again; after the first success it returns a cached completed task.
    /// </remarks>
    Task EnsureIndexesAsync(CancellationToken cancellationToken = default);
}
