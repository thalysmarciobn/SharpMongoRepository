using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using SharpMongoRepository.Internal;
using SharpMongoRepository.Querying;

namespace SharpMongoRepository;

/// <summary>
///     Default <see cref="IMongoRepository{TDocument}" /> implementation, for a document type with no
///     declared key.
/// </summary>
/// <typeparam name="TDocument">Document type. No interface or attribute has to be implemented.</typeparam>
/// <remarks>
///     <para>
///         Cheap to construct: it resolves nothing and opens nothing. The collection name from
///         <see cref="BsonCollectionAttribute" /> is cached in a <c>static readonly</c> field of the closed
///         generic type, so the attribute reflection happens once per document type for the lifetime of the
///         process rather than per instance.
///     </para>
///     <para>
///         Holds no unmanaged resources and is not disposable. The <see cref="IMongoClient" /> owns the
///         connection pool and is meant to outlive every repository built over it, which is why the
///         dependency-injection registration makes it a singleton.
///     </para>
///     <para>
///         Not sealed, because <see cref="MongoRepository{TDocument,TKey}" /> derives from it to add the
///         key-based operations.
///     </para>
/// </remarks>
public class MongoRepository<TDocument> : IMongoRepository<TDocument>
{
    /// <summary>
    ///     Collection name declared by <see cref="BsonCollectionAttribute" />, or <see langword="null" />
    ///     when the type carries no such attribute. Resolved once per closed generic type.
    /// </summary>
    private static readonly string? AttributeCollectionName = ResolveAttributeCollectionName();

    /// <summary>
    ///     Filter builder for this document type.
    /// </summary>
    protected static readonly FilterDefinitionBuilder<TDocument> Filters = Builders<TDocument>.Filter;

    /// <summary>
    ///     Cached delegate handed to each query, so starting one allocates no closure.
    /// </summary>
    private readonly Func<CancellationToken, Task> _ensureIndexesAsync;

    private readonly MongoIndexEnsurer _ensurer;
    private readonly ILogger _logger;

    /// <summary>
    ///     Gets the options this repository was configured with.
    /// </summary>
    protected MongoRepositoryOptions<TDocument> Options { get; }

    /// <summary>
    ///     Initializes a repository over a database you already have.
    /// </summary>
    /// <param name="database">Database holding the collection.</param>
    /// <param name="options">Repository options, or <see langword="null" /> for the defaults.</param>
    /// <param name="logger">Logger for index diagnostics, or <see langword="null" /> to discard them.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="database" /> is null.</exception>
    /// <exception cref="RepositoryException">
    ///     Thrown when no collection name is available from either
    ///     <see cref="MongoRepositoryOptions{TDocument}.CollectionName" /> or
    ///     <see cref="BsonCollectionAttribute" />.
    /// </exception>
    /// <remarks>
    ///     Prefer this overload. Passing in the database lets the caller decide how the
    ///     <see cref="IMongoClient" /> is shared, which is what the dependency-injection registration does.
    /// </remarks>
    public MongoRepository(
        IMongoDatabase database,
        MongoRepositoryOptions<TDocument>? options = null,
        ILogger? logger = null)
        : this(database, options, logger, new MongoIndexEnsurer())
    {
    }

    /// <summary>
    ///     Initializes a repository from a connection string.
    /// </summary>
    /// <param name="connectionString">MongoDB connection string.</param>
    /// <param name="databaseName">Name of the database.</param>
    /// <param name="options">Repository options, or <see langword="null" /> for the defaults.</param>
    /// <param name="logger">Logger for index diagnostics, or <see langword="null" /> to discard them.</param>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="connectionString" /> or <paramref name="databaseName" /> is empty.
    /// </exception>
    /// <exception cref="RepositoryException">Thrown when the connection string cannot be parsed.</exception>
    /// <remarks>
    ///     Convenience for console applications and tests. Each call builds an <see cref="IMongoClient" />;
    ///     the driver shares the underlying cluster between clients built from equal settings, but in a
    ///     hosted application register the repository through <c>AddMongoRepository</c> instead, so the
    ///     client is an explicit singleton.
    /// </remarks>
    public MongoRepository(
        string connectionString,
        string databaseName,
        MongoRepositoryOptions<TDocument>? options = null,
        ILogger? logger = null)
        : this(
            CreateDatabase(connectionString, databaseName, options),
            options,
            logger,
            new MongoIndexEnsurer())
    {
    }

    internal MongoRepository(
        IMongoDatabase database,
        MongoRepositoryOptions<TDocument>? options,
        ILogger? logger,
        MongoIndexEnsurer ensurer)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(ensurer);

        Options = options ?? new MongoRepositoryOptions<TDocument>();
        _logger = logger ?? NullLogger.Instance;
        _ensurer = ensurer;
        _ensureIndexesAsync = EnsureIndexesAsync;

        CollectionName = ResolveCollectionName(Options);

        if (Options.IgnoreExtraElements)
            ConfigureIgnoreExtraElements();

        var collection = database.GetCollection<TDocument>(CollectionName);

        // Only override what was explicitly configured, so a write concern, read concern or read
        // preference set on the connection string survives untouched.
        if (Options.WriteConcern is not null)
            collection = collection.WithWriteConcern(Options.WriteConcern);
        if (Options.ReadConcern is not null)
            collection = collection.WithReadConcern(Options.ReadConcern);
        if (Options.ReadPreference is not null)
            collection = collection.WithReadPreference(Options.ReadPreference);

        Collection = collection;
    }

    /// <inheritdoc />
    public IMongoCollection<TDocument> Collection { get; }

    /// <inheritdoc />
    public string CollectionName { get; }

    /// <inheritdoc />
    public IMongoQuery<TDocument> Query()
        => new MongoQuery<TDocument>(
            Collection, _ensureIndexesAsync, Options.OperationTimeout, Options.BatchSize, null);

    /// <inheritdoc />
    public IFindFluent<TDocument, TDocument> Find(
        FilterDefinition<TDocument> filter,
        IClientSessionHandle? session = null)
    {
        ArgumentNullException.ThrowIfNull(filter);
        EnsureIndexes(CancellationToken.None);
        return FindCore(filter, session);
    }

    /// <inheritdoc />
    public IQueryable<TDocument> AsQueryable(IClientSessionHandle? session = null)
    {
        EnsureIndexes(CancellationToken.None);
        return session is null ? Collection.AsQueryable() : Collection.AsQueryable(session);
    }

    /// <inheritdoc />
    public async Task<TDocument?> FindOneAsync(
        Expression<Func<TDocument, bool>> filter,
        IClientSessionHandle? session = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        await EnsureIndexesAsync(cancellationToken).ConfigureAwait(false);

        return await FindCore(Filters.Where(filter), session)
            .Limit(1)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public TDocument? FindOne(Expression<Func<TDocument, bool>> filter, IClientSessionHandle? session = null)
    {
        ArgumentNullException.ThrowIfNull(filter);
        EnsureIndexes(CancellationToken.None);
        return FindCore(Filters.Where(filter), session).Limit(1).FirstOrDefault();
    }

    /// <inheritdoc />
    public async Task<List<TDocument>> FilterByAsync(
        Expression<Func<TDocument, bool>> filter,
        IClientSessionHandle? session = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        await EnsureIndexesAsync(cancellationToken).ConfigureAwait(false);

        return await FindCore(Filters.Where(filter), session)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<List<TProjected>> FilterByAsync<TProjected>(
        Expression<Func<TDocument, bool>> filter,
        Expression<Func<TDocument, TProjected>> projection,
        IClientSessionHandle? session = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(projection);
        await EnsureIndexesAsync(cancellationToken).ConfigureAwait(false);

        return await FindCore(Filters.Where(filter), session)
            .Project(projection)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public IEnumerable<TDocument> FilterBy(
        Expression<Func<TDocument, bool>> filter,
        IClientSessionHandle? session = null)
    {
        ArgumentNullException.ThrowIfNull(filter);
        EnsureIndexes(CancellationToken.None);
        return FindCore(Filters.Where(filter), session).ToList();
    }

    /// <inheritdoc />
    public IEnumerable<TProjected> FilterBy<TProjected>(
        Expression<Func<TDocument, bool>> filter,
        Expression<Func<TDocument, TProjected>> projection,
        IClientSessionHandle? session = null)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(projection);
        EnsureIndexes(CancellationToken.None);
        return FindCore(Filters.Where(filter), session).Project(projection).ToList();
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<TDocument> StreamAsync(
        Expression<Func<TDocument, bool>>? filter = null,
        IClientSessionHandle? session = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await EnsureIndexesAsync(cancellationToken).ConfigureAwait(false);

        // `using` guarantees the cursor is released even when the consumer breaks out of the loop.
        using var cursor = await FindCore(ToFilter(filter), session)
            .ToCursorAsync(cancellationToken)
            .ConfigureAwait(false);

        while (await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
            foreach (var document in cursor.Current)
                yield return document;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<TProjected> StreamAsync<TProjected>(
        Expression<Func<TDocument, bool>>? filter,
        Expression<Func<TDocument, TProjected>> projection,
        IClientSessionHandle? session = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projection);
        await EnsureIndexesAsync(cancellationToken).ConfigureAwait(false);

        using var cursor = await FindCore(ToFilter(filter), session)
            .Project(projection)
            .ToCursorAsync(cancellationToken)
            .ConfigureAwait(false);

        while (await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
            foreach (var projected in cursor.Current)
                yield return projected;
    }

    /// <inheritdoc />
    public async Task<PagedResult<TDocument>> GetPageAsync(
        int page,
        int pageSize,
        Expression<Func<TDocument, bool>>? filter = null,
        SortDefinition<TDocument>? sort = null,
        IClientSessionHandle? session = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        if (page < 1)
            page = 1;

        await EnsureIndexesAsync(cancellationToken).ConfigureAwait(false);

        var definition = ToFilter(filter);
        var total = await CountCore(definition, session, cancellationToken).ConfigureAwait(false);

        var fluent = FindCore(definition, session);
        if (sort is not null)
            fluent = fluent.Sort(sort);

        var items = await fluent
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PagedResult<TDocument>(items, total, page, pageSize);
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(
        Expression<Func<TDocument, bool>> filter,
        IClientSessionHandle? session = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        await EnsureIndexesAsync(cancellationToken).ConfigureAwait(false);

        // Limit to one document and project away everything but _id: the server stops at the first match
        // and sends back a few bytes rather than a document.
        var found = await FindCore(Filters.Where(filter), session)
            .Limit(1)
            .Project<BsonDocument>(Builders<TDocument>.Projection.Include("_id"))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return found is not null;
    }

    /// <inheritdoc />
    public async Task<long> CountAsync(
        Expression<Func<TDocument, bool>>? filter = null,
        IClientSessionHandle? session = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureIndexesAsync(cancellationToken).ConfigureAwait(false);
        return await CountCore(ToFilter(filter), session, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public long Count(Expression<Func<TDocument, bool>>? filter = null, IClientSessionHandle? session = null)
    {
        EnsureIndexes(CancellationToken.None);
        var definition = ToFilter(filter);
        var options = CountOptionsOrNull();

        return session is null
            ? Collection.CountDocuments(definition, options)
            : Collection.CountDocuments(session, definition, options);
    }

    /// <inheritdoc />
    public Task<long> EstimateCountAsync(CancellationToken cancellationToken = default)
    {
        var options = Options.OperationTimeout is { } timeout
            ? new EstimatedDocumentCountOptions { MaxTime = timeout }
            : null;

        return Collection.EstimatedDocumentCountAsync(options, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<List<TField>> DistinctAsync<TField>(
        Expression<Func<TDocument, TField>> field,
        Expression<Func<TDocument, bool>>? filter = null,
        IClientSessionHandle? session = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(field);
        await EnsureIndexesAsync(cancellationToken).ConfigureAwait(false);

        var definition = ToFilter(filter);
        var options = Options.OperationTimeout is { } timeout
            ? new DistinctOptions { MaxTime = timeout }
            : null;

        var cursor = session is null
            ? await Collection.DistinctAsync(field, definition, options, cancellationToken)
                .ConfigureAwait(false)
            : await Collection.DistinctAsync(session, field, definition, options, cancellationToken)
                .ConfigureAwait(false);

        using (cursor)
        {
            return await cursor.ToListAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task InsertOneAsync(
        TDocument document,
        IClientSessionHandle? session = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        await EnsureIndexesAsync(cancellationToken).ConfigureAwait(false);

        AssignIdentifier(document);

        if (session is null)
            await Collection.InsertOneAsync(document, null, cancellationToken).ConfigureAwait(false);
        else
            await Collection.InsertOneAsync(session, document, null, cancellationToken)
                .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void InsertOne(TDocument document, IClientSessionHandle? session = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        EnsureIndexes(CancellationToken.None);

        AssignIdentifier(document);

        if (session is null)
            Collection.InsertOne(document);
        else
            Collection.InsertOne(session, document);
    }

    /// <inheritdoc />
    public async Task InsertManyAsync(
        IEnumerable<TDocument> documents,
        bool ordered = true,
        IClientSessionHandle? session = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documents);
        await EnsureIndexesAsync(cancellationToken).ConfigureAwait(false);

        // Materialise once: the sequence may be walked to assign keys and again by the driver, and a lazy
        // sequence would otherwise be enumerated twice.
        var batch = documents as IList<TDocument> ?? [.. documents];
        if (batch.Count == 0)
            return;

        AssignIdentifiers(batch);

        var options = new InsertManyOptions { IsOrdered = ordered };

        if (session is null)
            await Collection.InsertManyAsync(batch, options, cancellationToken).ConfigureAwait(false);
        else
            await Collection.InsertManyAsync(session, batch, options, cancellationToken)
                .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void InsertMany(
        IEnumerable<TDocument> documents,
        bool ordered = true,
        IClientSessionHandle? session = null)
    {
        ArgumentNullException.ThrowIfNull(documents);
        EnsureIndexes(CancellationToken.None);

        var batch = documents as IList<TDocument> ?? [.. documents];
        if (batch.Count == 0)
            return;

        AssignIdentifiers(batch);

        var options = new InsertManyOptions { IsOrdered = ordered };

        if (session is null)
            Collection.InsertMany(batch, options);
        else
            Collection.InsertMany(session, batch, options);
    }

    /// <inheritdoc />
    public Task<ReplaceOneResult> ReplaceOneAsync(
        Expression<Func<TDocument, bool>> filter,
        TDocument replacement,
        bool upsert = false,
        IClientSessionHandle? session = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(replacement);
        return ReplaceOneCoreAsync(
            Filters.Where(filter), replacement, upsert, session, cancellationToken);
    }

    /// <inheritdoc />
    public Task<TDocument?> FindOneAndReplaceAsync(
        Expression<Func<TDocument, bool>> filter,
        TDocument replacement,
        ReturnDocument returnDocument = ReturnDocument.After,
        bool upsert = false,
        IClientSessionHandle? session = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(replacement);
        return FindOneAndReplaceCoreAsync(
            Filters.Where(filter), replacement, returnDocument, upsert, session, cancellationToken);
    }

    /// <inheritdoc />
    public Task<UpdateResult> UpdateOneAsync(
        Expression<Func<TDocument, bool>> filter,
        UpdateDefinition<TDocument> update,
        bool upsert = false,
        IClientSessionHandle? session = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(update);
        return UpdateOneCoreAsync(Filters.Where(filter), update, upsert, session, cancellationToken);
    }

    /// <inheritdoc />
    public UpdateResult UpdateOne(
        Expression<Func<TDocument, bool>> filter,
        UpdateDefinition<TDocument> update,
        bool upsert = false,
        IClientSessionHandle? session = null)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(update);
        EnsureIndexes(CancellationToken.None);

        var definition = Filters.Where(filter);
        var options = new UpdateOptions { IsUpsert = upsert };

        return session is null
            ? Collection.UpdateOne(definition, update, options)
            : Collection.UpdateOne(session, definition, update, options);
    }

    /// <inheritdoc />
    public async Task<UpdateResult> UpdateManyAsync(
        Expression<Func<TDocument, bool>> filter,
        UpdateDefinition<TDocument> update,
        IClientSessionHandle? session = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(update);
        await EnsureIndexesAsync(cancellationToken).ConfigureAwait(false);

        var definition = Filters.Where(filter);

        return session is null
            ? await Collection.UpdateManyAsync(definition, update, null, cancellationToken)
                .ConfigureAwait(false)
            : await Collection.UpdateManyAsync(session, definition, update, null, cancellationToken)
                .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<TDocument?> FindOneAndUpdateAsync(
        Expression<Func<TDocument, bool>> filter,
        UpdateDefinition<TDocument> update,
        ReturnDocument returnDocument = ReturnDocument.After,
        bool upsert = false,
        IClientSessionHandle? session = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(update);
        await EnsureIndexesAsync(cancellationToken).ConfigureAwait(false);

        var definition = Filters.Where(filter);
        var options = new FindOneAndUpdateOptions<TDocument>
        {
            ReturnDocument = returnDocument,
            IsUpsert = upsert,
            MaxTime = Options.OperationTimeout
        };

        return session is null
            ? await Collection.FindOneAndUpdateAsync(definition, update, options, cancellationToken)
                .ConfigureAwait(false)
            : await Collection
                .FindOneAndUpdateAsync(session, definition, update, options, cancellationToken)
                .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<long> DeleteOneAsync(
        Expression<Func<TDocument, bool>> filter,
        IClientSessionHandle? session = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return DeleteOneCoreAsync(Filters.Where(filter), session, cancellationToken);
    }

    /// <inheritdoc />
    public long DeleteOne(Expression<Func<TDocument, bool>> filter, IClientSessionHandle? session = null)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return DeleteOneCore(Filters.Where(filter), session);
    }

    /// <inheritdoc />
    public async Task<long> DeleteManyAsync(
        Expression<Func<TDocument, bool>> filter,
        IClientSessionHandle? session = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        await EnsureIndexesAsync(cancellationToken).ConfigureAwait(false);

        var definition = Filters.Where(filter);

        var result = session is null
            ? await Collection.DeleteManyAsync(definition, cancellationToken).ConfigureAwait(false)
            : await Collection.DeleteManyAsync(session, definition, null, cancellationToken)
                .ConfigureAwait(false);

        return result.IsAcknowledged ? result.DeletedCount : 0;
    }

    /// <inheritdoc />
    public long DeleteMany(Expression<Func<TDocument, bool>> filter, IClientSessionHandle? session = null)
    {
        ArgumentNullException.ThrowIfNull(filter);
        EnsureIndexes(CancellationToken.None);

        var definition = Filters.Where(filter);

        var result = session is null
            ? Collection.DeleteMany(definition)
            : Collection.DeleteMany(session, definition, null);

        return result.IsAcknowledged ? result.DeletedCount : 0;
    }

    /// <inheritdoc />
    public async Task<TDocument?> FindOneAndDeleteAsync(
        Expression<Func<TDocument, bool>> filter,
        IClientSessionHandle? session = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        await EnsureIndexesAsync(cancellationToken).ConfigureAwait(false);

        var definition = Filters.Where(filter);
        var options = new FindOneAndDeleteOptions<TDocument> { MaxTime = Options.OperationTimeout };

        return session is null
            ? await Collection.FindOneAndDeleteAsync(definition, options, cancellationToken)
                .ConfigureAwait(false)
            : await Collection.FindOneAndDeleteAsync(session, definition, options, cancellationToken)
                .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<BulkWriteResult<TDocument>> BulkWriteAsync(
        IEnumerable<WriteModel<TDocument>> requests,
        bool ordered = true,
        IClientSessionHandle? session = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests);
        await EnsureIndexesAsync(cancellationToken).ConfigureAwait(false);

        var options = new BulkWriteOptions { IsOrdered = ordered };

        return session is null
            ? await Collection.BulkWriteAsync(requests, options, cancellationToken).ConfigureAwait(false)
            : await Collection.BulkWriteAsync(session, requests, options, cancellationToken)
                .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<TResult> WithTransactionAsync<TResult>(
        Func<IClientSessionHandle, CancellationToken, Task<TResult>> body,
        TransactionOptions? transactionOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(body);
        await EnsureIndexesAsync(cancellationToken).ConfigureAwait(false);

        using var session = await Collection.Database.Client
            .StartSessionAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        // The driver's runner owns the start, commit, abort and retry rules: it replays the body on
        // TransientTransactionError and re-commits on UnknownTransactionCommitResult.
        return await session
            .WithTransactionAsync(body, transactionOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task WithTransactionAsync(
        Func<IClientSessionHandle, CancellationToken, Task> body,
        TransactionOptions? transactionOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(body);

        return WithTransactionAsync<object?>(
            async (session, token) =>
            {
                await body(session, token).ConfigureAwait(false);
                return null;
            },
            transactionOptions,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IClientSessionHandle> StartSessionAsync(CancellationToken cancellationToken = default)
        => await Collection.Database.Client
            .StartSessionAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public Task EnsureIndexesAsync(CancellationToken cancellationToken = default)
        => _ensurer.EnsureAsync(Collection, Options, _logger, cancellationToken);

    /// <summary>
    ///     Assigns a generated identifier to one document before insert.
    /// </summary>
    /// <param name="document">Document about to be inserted.</param>
    /// <remarks>
    ///     Does nothing here, because a keyless document has no key this type knows how to reach.
    ///     <see cref="MongoRepository{TDocument,TKey}" /> overrides it.
    /// </remarks>
    protected virtual void AssignIdentifier(TDocument document)
    {
    }

    /// <summary>
    ///     Assigns generated identifiers to a batch before insert.
    /// </summary>
    /// <param name="documents">Documents about to be inserted.</param>
    /// <remarks>
    ///     Does nothing here. <see cref="MongoRepository{TDocument,TKey}" /> overrides it, and must, because
    ///     the driver registers no identifier generator for a <see cref="string" /> key.
    /// </remarks>
    protected virtual void AssignIdentifiers(IList<TDocument> documents)
    {
    }

    /// <summary>
    ///     Ensures the configured indexes exist, synchronously.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    protected void EnsureIndexes(CancellationToken cancellationToken)
        => _ensurer.Ensure(Collection, Options, _logger, cancellationToken);

    /// <summary>
    ///     Builds a fluent find carrying the configured server-side timeout and batch size.
    /// </summary>
    /// <param name="filter">Filter to apply.</param>
    /// <param name="session">Session to enrol the read in, or <see langword="null" />.</param>
    /// <returns>The configured fluent find.</returns>
    protected IFindFluent<TDocument, TDocument> FindCore(
        FilterDefinition<TDocument> filter,
        IClientSessionHandle? session)
    {
        var fluent = session is null
            ? Collection.Find(filter)
            : Collection.Find(session, filter);

        if (Options.OperationTimeout is { } timeout)
            fluent.Options.MaxTime = timeout;

        if (Options.BatchSize is { } batchSize)
            fluent.Options.BatchSize = batchSize;

        return fluent;
    }

    /// <summary>
    ///     Replaces one document, selected by a filter.
    /// </summary>
    protected async Task<ReplaceOneResult> ReplaceOneCoreAsync(
        FilterDefinition<TDocument> filter,
        TDocument replacement,
        bool upsert,
        IClientSessionHandle? session,
        CancellationToken cancellationToken)
    {
        await EnsureIndexesAsync(cancellationToken).ConfigureAwait(false);

        var options = new ReplaceOptions { IsUpsert = upsert };

        return session is null
            ? await Collection.ReplaceOneAsync(filter, replacement, options, cancellationToken)
                .ConfigureAwait(false)
            : await Collection.ReplaceOneAsync(session, filter, replacement, options, cancellationToken)
                .ConfigureAwait(false);
    }

    /// <summary>
    ///     Replaces one document, selected by a filter, and returns one version of it.
    /// </summary>
    protected async Task<TDocument?> FindOneAndReplaceCoreAsync(
        FilterDefinition<TDocument> filter,
        TDocument replacement,
        ReturnDocument returnDocument,
        bool upsert,
        IClientSessionHandle? session,
        CancellationToken cancellationToken)
    {
        await EnsureIndexesAsync(cancellationToken).ConfigureAwait(false);

        var options = new FindOneAndReplaceOptions<TDocument>
        {
            ReturnDocument = returnDocument,
            IsUpsert = upsert,
            MaxTime = Options.OperationTimeout
        };

        return session is null
            ? await Collection.FindOneAndReplaceAsync(filter, replacement, options, cancellationToken)
                .ConfigureAwait(false)
            : await Collection
                .FindOneAndReplaceAsync(session, filter, replacement, options, cancellationToken)
                .ConfigureAwait(false);
    }

    /// <summary>
    ///     Applies a partial update to one document, selected by a filter.
    /// </summary>
    protected async Task<UpdateResult> UpdateOneCoreAsync(
        FilterDefinition<TDocument> filter,
        UpdateDefinition<TDocument> update,
        bool upsert,
        IClientSessionHandle? session,
        CancellationToken cancellationToken)
    {
        await EnsureIndexesAsync(cancellationToken).ConfigureAwait(false);

        var options = new UpdateOptions { IsUpsert = upsert };

        return session is null
            ? await Collection.UpdateOneAsync(filter, update, options, cancellationToken)
                .ConfigureAwait(false)
            : await Collection.UpdateOneAsync(session, filter, update, options, cancellationToken)
                .ConfigureAwait(false);
    }

    /// <summary>
    ///     Deletes one document, selected by a filter.
    /// </summary>
    protected async Task<long> DeleteOneCoreAsync(
        FilterDefinition<TDocument> filter,
        IClientSessionHandle? session,
        CancellationToken cancellationToken)
    {
        await EnsureIndexesAsync(cancellationToken).ConfigureAwait(false);

        var result = session is null
            ? await Collection.DeleteOneAsync(filter, cancellationToken).ConfigureAwait(false)
            : await Collection.DeleteOneAsync(session, filter, null, cancellationToken)
                .ConfigureAwait(false);

        return result.IsAcknowledged ? result.DeletedCount : 0;
    }

    /// <summary>
    ///     Deletes one document, selected by a filter, synchronously.
    /// </summary>
    protected long DeleteOneCore(FilterDefinition<TDocument> filter, IClientSessionHandle? session)
    {
        EnsureIndexes(CancellationToken.None);

        var result = session is null
            ? Collection.DeleteOne(filter)
            : Collection.DeleteOne(session, filter, null);

        return result.IsAcknowledged ? result.DeletedCount : 0;
    }

    /// <summary>
    ///     Builds a query already narrowed by a filter.
    /// </summary>
    protected IMongoQuery<TDocument> QueryCore(FilterDefinition<TDocument> filter)
        => new MongoQuery<TDocument>(
                Collection, _ensureIndexesAsync, Options.OperationTimeout, Options.BatchSize, null)
            .Where(filter);

    private Task<long> CountCore(
        FilterDefinition<TDocument> filter,
        IClientSessionHandle? session,
        CancellationToken cancellationToken)
    {
        var options = CountOptionsOrNull();

        return session is null
            ? Collection.CountDocumentsAsync(filter, options, cancellationToken)
            : Collection.CountDocumentsAsync(session, filter, options, cancellationToken);
    }

    private CountOptions? CountOptionsOrNull()
        => Options.OperationTimeout is { } timeout ? new CountOptions { MaxTime = timeout } : null;

    private static FilterDefinition<TDocument> ToFilter(Expression<Func<TDocument, bool>>? filter)
        => filter is null ? FilterDefinition<TDocument>.Empty : Filters.Where(filter);

    /// <summary>
    ///     Builds the database handle for the connection-string constructor.
    /// </summary>
    private static IMongoDatabase CreateDatabase(
        string connectionString,
        string databaseName,
        MongoRepositoryOptions<TDocument>? options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);

        MongoClientSettings settings;
        try
        {
            settings = MongoClientSettings.FromConnectionString(connectionString);
        }
        catch (Exception exception)
        {
            throw new RepositoryException(
                "The MongoDB connection string could not be parsed.", exception);
        }

        options?.ConfigureClientSettings?.Invoke(settings);

        return new MongoClient(settings).GetDatabase(databaseName);
    }

    /// <summary>
    ///     Works out which collection to use, preferring the options over the attribute.
    /// </summary>
    /// <exception cref="RepositoryException">Thrown when neither source provides a name.</exception>
    private static string ResolveCollectionName(MongoRepositoryOptions<TDocument> options)
    {
        if (!string.IsNullOrWhiteSpace(options.CollectionName))
            return options.CollectionName;

        if (!string.IsNullOrWhiteSpace(AttributeCollectionName))
            return AttributeCollectionName;

        throw new RepositoryException(
            $"No collection name for '{typeof(TDocument).Name}'. Either decorate the type with " +
            "[BsonCollection(\"name\")], or set CollectionName on the repository options, which is how a " +
            "type you do not own can be used as a document.");
    }

    /// <summary>
    ///     Registers a class map for <typeparamref name="TDocument" /> that tolerates unmapped elements.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Needed by a document type with no identifier member: the server stores an <c>_id</c> on every
    ///         document, and the default mapping rejects an element it cannot place.
    ///     </para>
    ///     <para>
    ///         <c>TryRegisterClassMap</c> is a no-op when a map is already registered, so an explicit
    ///         mapping set up by the application wins and calling this twice is harmless. Types the driver
    ///         serialises without a class map are skipped, because registering one for
    ///         <c>BsonDocument</c> would replace a serializer that already keeps every element.
    ///     </para>
    /// </remarks>
    private static void ConfigureIgnoreExtraElements()
    {
        var type = typeof(TDocument);

        if (!type.IsClass || typeof(BsonValue).IsAssignableFrom(type) || type == typeof(object))
            return;

        BsonClassMap.TryRegisterClassMap<TDocument>(map =>
        {
            map.AutoMap();
            map.SetIgnoreExtraElements(true);
        });
    }

    /// <summary>
    ///     Reads the collection name off <see cref="BsonCollectionAttribute" />, if present.
    /// </summary>
    /// <remarks>
    ///     Returns <see langword="null" /> rather than throwing when the attribute is absent, because the
    ///     options may supply the name instead. The decision is deferred to
    ///     <see cref="ResolveCollectionName" />, which can see both.
    /// </remarks>
    private static string? ResolveAttributeCollectionName()
    {
        var attribute = typeof(TDocument).GetCustomAttribute<BsonCollectionAttribute>(false);
        return string.IsNullOrWhiteSpace(attribute?.CollectionName) ? null : attribute.CollectionName;
    }
}
