using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using SharpMongoRepository.Internal;

namespace SharpMongoRepository;

/// <summary>
///     Default <see cref="IMongoRepository{TDocument,TKey}" /> implementation.
/// </summary>
/// <typeparam name="TDocument">Document type, which must implement <see cref="IDocument{TKey}" />.</typeparam>
/// <typeparam name="TKey">Type of the document's primary key.</typeparam>
/// <remarks>
///     <para>
///         The type is cheap to construct: it resolves nothing and opens nothing. The collection name and the
///         <c>_id</c> field definition live in <c>static readonly</c> fields, so the runtime initialises them
///         once per closed generic type; the attribute reflection and the key expression tree are each built
///         a single time rather than per instance or per call.
///     </para>
///     <para>
///         It holds no unmanaged resources and is not disposable. The <see cref="IMongoClient" /> owns the
///         connection pool and is meant to outlive every repository built over it, which is why the
///         dependency-injection registration makes it a singleton.
///     </para>
/// </remarks>
public sealed class MongoRepository<TDocument, TKey> : IMongoRepository<TDocument, TKey>
    where TDocument : IDocument<TKey>
    where TKey : notnull
{
    /// <summary>
    ///     Collection name from <see cref="BsonCollectionAttribute" />, resolved once per closed generic type.
    /// </summary>
    public static string CollectionName { get; } = ResolveCollectionName();

    /// <summary>
    ///     Selector for the primary key, built once per closed generic type.
    /// </summary>
    /// <remarks>
    ///     The previous version called <c>Builders&lt;TDocument&gt;.Filter.Eq(doc =&gt; doc.Id, id)</c> inside
    ///     every key-based method. An expression tree is constructed afresh on each evaluation of such a
    ///     lambda, so that allocated a new tree per call; hoisting it into a <c>static readonly</c> field
    ///     builds it exactly once.
    /// </remarks>
    private static readonly Expression<Func<TDocument, TKey>> IdSelector = document => document.Id;

    /// <summary>
    ///     The primary key field.
    /// </summary>
    /// <remarks>
    ///     Deliberately an expression field definition rather than the string <c>"_id"</c>. A string field
    ///     definition resolves no member, so it serialises the value with the registry's default serializer
    ///     for <typeparamref name="TKey" />; for a <see cref="Guid" /> key that throws
    ///     (<c>GuidSerializer cannot serialize a Guid when GuidRepresentation is Unspecified</c>), and a key
    ///     carrying <c>[BsonRepresentation(BsonType.String)]</c> would be matched against the wrong BSON
    ///     type. Going through the expression lets the driver resolve the member's own serializer.
    /// </remarks>
    private static readonly FieldDefinition<TDocument, TKey> IdField =
        new ExpressionFieldDefinition<TDocument, TKey>(IdSelector);

    private static readonly FilterDefinitionBuilder<TDocument> Filters = Builders<TDocument>.Filter;

    private readonly MongoIndexEnsurer _ensurer;
    private readonly ILogger _logger;
    private readonly MongoRepositoryOptions<TDocument, TKey> _options;

    /// <summary>
    ///     Initializes a repository over a database you already have.
    /// </summary>
    /// <param name="database">Database holding the collection.</param>
    /// <param name="options">Repository options, or <see langword="null" /> for the defaults.</param>
    /// <param name="logger">Logger for index diagnostics, or <see langword="null" /> to discard them.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="database" /> is null.</exception>
    /// <exception cref="RepositoryException">
    ///     Thrown when <typeparamref name="TDocument" /> carries no <see cref="BsonCollectionAttribute" />.
    /// </exception>
    /// <remarks>
    ///     Prefer this overload. Passing in the database lets the caller decide how the
    ///     <see cref="IMongoClient" /> is shared, which is what the dependency-injection registration does.
    /// </remarks>
    public MongoRepository(
        IMongoDatabase database,
        MongoRepositoryOptions<TDocument, TKey>? options = null,
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
    ///     the driver shares the underlying cluster between clients built from equal settings, but in a hosted
    ///     application register the repository through <c>AddMongoRepository</c> instead, so the client is an
    ///     explicit singleton.
    /// </remarks>
    public MongoRepository(
        string connectionString,
        string databaseName,
        MongoRepositoryOptions<TDocument, TKey>? options = null,
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
        MongoRepositoryOptions<TDocument, TKey>? options,
        ILogger? logger,
        MongoIndexEnsurer ensurer)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(ensurer);

        _options = options ?? new MongoRepositoryOptions<TDocument, TKey>();
        _logger = logger ?? NullLogger.Instance;
        _ensurer = ensurer;

        var collection = database.GetCollection<TDocument>(CollectionName);

        // Only override what was explicitly configured, so a write concern, read concern or read
        // preference set on the connection string survives untouched.
        if (_options.WriteConcern is not null)
            collection = collection.WithWriteConcern(_options.WriteConcern);
        if (_options.ReadConcern is not null)
            collection = collection.WithReadConcern(_options.ReadConcern);
        if (_options.ReadPreference is not null)
            collection = collection.WithReadPreference(_options.ReadPreference);

        Collection = collection;
    }

    /// <inheritdoc />
    public IMongoCollection<TDocument> Collection { get; }

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
    public async Task<TDocument?> FindByIdAsync(
        TKey id,
        IClientSessionHandle? session = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);
        await EnsureIndexesAsync(cancellationToken).ConfigureAwait(false);

        return await FindCore(Filters.Eq(IdField, id), session)
            .Limit(1)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public TDocument? FindById(TKey id, IClientSessionHandle? session = null)
    {
        ArgumentNullException.ThrowIfNull(id);
        EnsureIndexes(CancellationToken.None);
        return FindCore(Filters.Eq(IdField, id), session).Limit(1).FirstOrDefault();
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
        var options = _options.OperationTimeout is { } timeout
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
        var options = _options.OperationTimeout is { } timeout
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

        IdAssigner<TDocument, TKey>.Assign(document, _options.IdGenerator);

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

        IdAssigner<TDocument, TKey>.Assign(document, _options.IdGenerator);

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

        // Materialise once: the sequence is walked to assign keys and again by the driver, and a lazy
        // sequence would otherwise be enumerated twice.
        var batch = documents as IList<TDocument> ?? [.. documents];
        if (batch.Count == 0)
            return;

        IdAssigner<TDocument, TKey>.Assign(batch, _options.IdGenerator);

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

        IdAssigner<TDocument, TKey>.Assign(batch, _options.IdGenerator);

        var options = new InsertManyOptions { IsOrdered = ordered };

        if (session is null)
            Collection.InsertMany(batch, options);
        else
            Collection.InsertMany(session, batch, options);
    }

    /// <inheritdoc />
    public async Task<ReplaceOneResult> ReplaceOneAsync(
        TDocument document,
        bool upsert = false,
        IClientSessionHandle? session = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        await EnsureIndexesAsync(cancellationToken).ConfigureAwait(false);

        if (upsert)
            IdAssigner<TDocument, TKey>.Assign(document, _options.IdGenerator);

        var filter = Filters.Eq(IdField, document.Id);
        var options = new ReplaceOptions { IsUpsert = upsert };

        return session is null
            ? await Collection.ReplaceOneAsync(filter, document, options, cancellationToken)
                .ConfigureAwait(false)
            : await Collection.ReplaceOneAsync(session, filter, document, options, cancellationToken)
                .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<TDocument?> FindOneAndReplaceAsync(
        TDocument document,
        ReturnDocument returnDocument = ReturnDocument.After,
        bool upsert = false,
        IClientSessionHandle? session = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        await EnsureIndexesAsync(cancellationToken).ConfigureAwait(false);

        if (upsert)
            IdAssigner<TDocument, TKey>.Assign(document, _options.IdGenerator);

        var filter = Filters.Eq(IdField, document.Id);
        var options = new FindOneAndReplaceOptions<TDocument>
        {
            ReturnDocument = returnDocument,
            IsUpsert = upsert,
            MaxTime = _options.OperationTimeout
        };

        return session is null
            ? await Collection.FindOneAndReplaceAsync(filter, document, options, cancellationToken)
                .ConfigureAwait(false)
            : await Collection
                .FindOneAndReplaceAsync(session, filter, document, options, cancellationToken)
                .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<UpdateResult> UpdateByIdAsync(
        TKey id,
        UpdateDefinition<TDocument> update,
        bool upsert = false,
        IClientSessionHandle? session = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(update);
        return UpdateOneCoreAsync(Filters.Eq(IdField, id), update, upsert, session, cancellationToken);
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
            MaxTime = _options.OperationTimeout
        };

        return session is null
            ? await Collection.FindOneAndUpdateAsync(definition, update, options, cancellationToken)
                .ConfigureAwait(false)
            : await Collection
                .FindOneAndUpdateAsync(session, definition, update, options, cancellationToken)
                .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<long> DeleteByIdAsync(
        TKey id,
        IClientSessionHandle? session = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);
        return DeleteOneCoreAsync(Filters.Eq(IdField, id), session, cancellationToken);
    }

    /// <inheritdoc />
    public long DeleteById(TKey id, IClientSessionHandle? session = null)
    {
        ArgumentNullException.ThrowIfNull(id);
        return DeleteOneCore(Filters.Eq(IdField, id), session);
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
        var options = new FindOneAndDeleteOptions<TDocument> { MaxTime = _options.OperationTimeout };

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
        => _ensurer.EnsureAsync(Collection, _options, _logger, cancellationToken);

    private void EnsureIndexes(CancellationToken cancellationToken)
        => _ensurer.Ensure(Collection, _options, _logger, cancellationToken);

    private IFindFluent<TDocument, TDocument> FindCore(
        FilterDefinition<TDocument> filter,
        IClientSessionHandle? session)
    {
        var fluent = session is null
            ? Collection.Find(filter)
            : Collection.Find(session, filter);

        if (_options.OperationTimeout is { } timeout)
            fluent.Options.MaxTime = timeout;

        return fluent;
    }

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

    private async Task<UpdateResult> UpdateOneCoreAsync(
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

    private async Task<long> DeleteOneCoreAsync(
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

    private long DeleteOneCore(FilterDefinition<TDocument> filter, IClientSessionHandle? session)
    {
        EnsureIndexes(CancellationToken.None);

        var result = session is null
            ? Collection.DeleteOne(filter)
            : Collection.DeleteOne(session, filter, null);

        return result.IsAcknowledged ? result.DeletedCount : 0;
    }

    private CountOptions? CountOptionsOrNull()
        => _options.OperationTimeout is { } timeout ? new CountOptions { MaxTime = timeout } : null;

    private static FilterDefinition<TDocument> ToFilter(Expression<Func<TDocument, bool>>? filter)
        => filter is null ? FilterDefinition<TDocument>.Empty : Filters.Where(filter);

    /// <summary>
    ///     Builds the database handle for the connection-string constructor.
    /// </summary>
    private static IMongoDatabase CreateDatabase(
        string connectionString,
        string databaseName,
        MongoRepositoryOptions<TDocument, TKey>? options)
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
    ///     Reads the collection name off <see cref="BsonCollectionAttribute" />.
    /// </summary>
    /// <exception cref="RepositoryException">
    ///     Thrown when the attribute is missing or carries an empty name.
    /// </exception>
    private static string ResolveCollectionName()
    {
        var attribute = typeof(TDocument).GetCustomAttribute<BsonCollectionAttribute>(false);

        if (attribute is null)
            throw new RepositoryException(
                $"'{typeof(TDocument).Name}' is missing [BsonCollection]. Add it to declare which " +
                "collection the type is stored in, for example [BsonCollection(\"orders\")].");

        if (string.IsNullOrWhiteSpace(attribute.CollectionName))
            throw new RepositoryException(
                $"[BsonCollection] on '{typeof(TDocument).Name}' has an empty collection name.");

        return attribute.CollectionName;
    }
}
