using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace SharpMongoRepository.Querying;

/// <summary>
///     Immutable <see cref="IMongoQuery{TDocument}" /> implementation over one collection.
/// </summary>
/// <typeparam name="TDocument">Document type being queried.</typeparam>
/// <remarks>
///     Each builder method copies the state and returns a new instance. The copy is a handful of field
///     assignments against one network round-trip, and it buys a guarantee worth far more than that: a
///     half-built query held in a field cannot be mutated by whoever branches from it.
/// </remarks>
internal sealed class MongoQuery<TDocument> : IMongoQuery<TDocument>
{
    private static readonly FilterDefinitionBuilder<TDocument> Filters = Builders<TDocument>.Filter;
    private static readonly SortDefinitionBuilder<TDocument> Sorts = Builders<TDocument>.Sort;

    private readonly bool? _allowDiskUse;
    private readonly int? _batchSize;
    private readonly IMongoCollection<TDocument> _collection;
    private readonly Func<CancellationToken, Task> _ensureIndexes;
    private readonly FilterDefinition<TDocument> _filter;
    private readonly BsonValue? _hint;
    private readonly int? _limit;
    private readonly TimeSpan? _maxTime;
    private readonly IClientSessionHandle? _session;
    private readonly int? _skip;
    private readonly SortDefinition<TDocument>? _sort;

    internal MongoQuery(
        IMongoCollection<TDocument> collection,
        Func<CancellationToken, Task> ensureIndexes,
        TimeSpan? maxTime,
        int? batchSize,
        IClientSessionHandle? session)
        : this(collection, ensureIndexes, FilterDefinition<TDocument>.Empty, null, null, null,
            null, batchSize, maxTime, null, session)
    {
    }

    private MongoQuery(
        IMongoCollection<TDocument> collection,
        Func<CancellationToken, Task> ensureIndexes,
        FilterDefinition<TDocument> filter,
        SortDefinition<TDocument>? sort,
        int? skip,
        int? limit,
        BsonValue? hint,
        int? batchSize,
        TimeSpan? maxTime,
        bool? allowDiskUse,
        IClientSessionHandle? session)
    {
        _collection = collection;
        _ensureIndexes = ensureIndexes;
        _filter = filter;
        _sort = sort;
        _skip = skip;
        _limit = limit;
        _hint = hint;
        _batchSize = batchSize;
        _maxTime = maxTime;
        _allowDiskUse = allowDiskUse;
        _session = session;
    }

    /// <inheritdoc />
    public IMongoQuery<TDocument> Where(Expression<Func<TDocument, bool>> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return Where(Filters.Where(predicate));
    }

    /// <inheritdoc />
    public IMongoQuery<TDocument> Where(FilterDefinition<TDocument> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return With(filter: Filters.And(_filter, filter));
    }

    /// <inheritdoc />
    public IMongoQuery<TDocument> WhereField<TValue>(string field, TValue value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        return Where(Filters.Eq(new StringFieldDefinition<TDocument, TValue>(field), value));
    }

    /// <inheritdoc />
    public IMongoQuery<TDocument> WhereAny(
        params ReadOnlySpan<Expression<Func<TDocument, bool>>> predicates)
    {
        if (predicates.IsEmpty)
            throw new ArgumentException("At least one predicate is required.", nameof(predicates));

        var alternatives = new FilterDefinition<TDocument>[predicates.Length];
        for (var i = 0; i < predicates.Length; i++)
        {
            var predicate = predicates[i]
                            ?? throw new ArgumentException(
                                $"Predicate at position {i} is null.", nameof(predicates));
            alternatives[i] = Filters.Where(predicate);
        }

        // The group is combined with `and` against what is already there, rather than widening the whole
        // query to an `or`, which is the mistake this method exists to prevent.
        return Where(Filters.Or(alternatives));
    }

    /// <inheritdoc />
    public IMongoQuery<TDocument> OrderBy<TField>(Expression<Func<TDocument, TField>> field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return With(sort: Sorts.Ascending(ToField(field)));
    }

    /// <inheritdoc />
    public IMongoQuery<TDocument> OrderByDescending<TField>(Expression<Func<TDocument, TField>> field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return With(sort: Sorts.Descending(ToField(field)));
    }

    /// <inheritdoc />
    public IMongoQuery<TDocument> ThenBy<TField>(Expression<Func<TDocument, TField>> field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return With(sort: Combine(Sorts.Ascending(ToField(field))));
    }

    /// <inheritdoc />
    public IMongoQuery<TDocument> ThenByDescending<TField>(Expression<Func<TDocument, TField>> field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return With(sort: Combine(Sorts.Descending(ToField(field))));
    }

    /// <inheritdoc />
    public IMongoQuery<TDocument> OrderBy(SortDefinition<TDocument> sort)
    {
        ArgumentNullException.ThrowIfNull(sort);
        return With(sort: sort);
    }

    /// <inheritdoc />
    public IMongoQuery<TDocument> Skip(int count) => With(skip: Math.Max(count, 0));

    /// <inheritdoc />
    public IMongoQuery<TDocument> Take(int count) => With(limit: Math.Max(count, 0));

    /// <inheritdoc />
    public IMongoQuery<TDocument> WithHint(string indexName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(indexName);
        return With(hint: BsonValue.Create(indexName));
    }

    /// <inheritdoc />
    public IMongoQuery<TDocument> WithHint(BsonDocument keyPattern)
    {
        ArgumentNullException.ThrowIfNull(keyPattern);
        return With(hint: keyPattern);
    }

    /// <inheritdoc />
    public IMongoQuery<TDocument> WithBatchSize(int size)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 1);
        return With(batchSize: size);
    }

    /// <inheritdoc />
    public IMongoQuery<TDocument> WithMaxTime(TimeSpan maxTime) => With(maxTime: maxTime);

    /// <inheritdoc />
    public IMongoQuery<TDocument> AllowDiskUse() => With(allowDiskUse: true);

    /// <inheritdoc />
    public IMongoQuery<TDocument> InSession(IClientSessionHandle session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return With(session: session);
    }

    /// <inheritdoc />
    public async Task<List<TDocument>> ToListAsync(CancellationToken cancellationToken = default)
    {
        await _ensureIndexes(cancellationToken).ConfigureAwait(false);
        return await Fluent().ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<List<TProjected>> ToListAsync<TProjected>(
        Expression<Func<TDocument, TProjected>> projection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projection);
        await _ensureIndexes(cancellationToken).ConfigureAwait(false);

        return await Fluent().Project(projection).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<TDocument> ToAsyncEnumerable(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await _ensureIndexes(cancellationToken).ConfigureAwait(false);

        using var cursor = await Fluent().ToCursorAsync(cancellationToken).ConfigureAwait(false);

        while (await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
            foreach (var document in cursor.Current)
                yield return document;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<TProjected> ToAsyncEnumerable<TProjected>(
        Expression<Func<TDocument, TProjected>> projection,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projection);
        await _ensureIndexes(cancellationToken).ConfigureAwait(false);

        using var cursor = await Fluent()
            .Project(projection)
            .ToCursorAsync(cancellationToken)
            .ConfigureAwait(false);

        while (await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
            foreach (var projected in cursor.Current)
                yield return projected;
    }

    /// <inheritdoc />
    public async Task<TDocument?> FirstOrDefaultAsync(CancellationToken cancellationToken = default)
    {
        await _ensureIndexes(cancellationToken).ConfigureAwait(false);

        return await Fluent(limitOverride: 1)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<TProjected?> FirstOrDefaultAsync<TProjected>(
        Expression<Func<TDocument, TProjected>> projection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projection);
        await _ensureIndexes(cancellationToken).ConfigureAwait(false);

        return await Fluent(limitOverride: 1)
            .Project(projection)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<TDocument?> SingleOrDefaultAsync(CancellationToken cancellationToken = default)
    {
        await _ensureIndexes(cancellationToken).ConfigureAwait(false);

        // Two documents, so a second match can be reported rather than silently dropped.
        var found = await Fluent(limitOverride: 2).ToListAsync(cancellationToken).ConfigureAwait(false);

        return found.Count switch
        {
            0 => default,
            1 => found[0],
            _ => throw new InvalidOperationException(
                $"The query matched more than one document in '{_collection.CollectionNamespace.FullName}'.")
        };
    }

    /// <inheritdoc />
    public async Task<long> CountAsync(CancellationToken cancellationToken = default)
    {
        await _ensureIndexes(cancellationToken).ConfigureAwait(false);
        return await CountCoreAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> AnyAsync(CancellationToken cancellationToken = default)
    {
        await _ensureIndexes(cancellationToken).ConfigureAwait(false);

        var found = await Fluent(limitOverride: 1)
            .Project<BsonDocument>(Builders<TDocument>.Projection.Include("_id"))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return found is not null;
    }

    /// <inheritdoc />
    public async Task<PagedResult<TDocument>> ToPageAsync(
        int page,
        int pageSize,
        bool includeTotal = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        page = Math.Max(page, 1);

        await _ensureIndexes(cancellationToken).ConfigureAwait(false);

        var total = includeTotal
            ? await CountCoreAsync(cancellationToken).ConfigureAwait(false)
            : PagedResult<TDocument>.TotalCountNotCounted;

        var items = await Fluent(skipOverride: (page - 1) * pageSize, limitOverride: pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PagedResult<TDocument>(items, total, page, pageSize);
    }

    /// <inheritdoc />
    public async Task<PagedResult<TProjected>> ToPageAsync<TProjected>(
        int page,
        int pageSize,
        Expression<Func<TDocument, TProjected>> projection,
        bool includeTotal = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        page = Math.Max(page, 1);

        await _ensureIndexes(cancellationToken).ConfigureAwait(false);

        var total = includeTotal
            ? await CountCoreAsync(cancellationToken).ConfigureAwait(false)
            : PagedResult<TProjected>.TotalCountNotCounted;

        var items = await Fluent(skipOverride: (page - 1) * pageSize, limitOverride: pageSize)
            .Project(projection)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PagedResult<TProjected>(items, total, page, pageSize);
    }

    /// <inheritdoc />
    public IFindFluent<TDocument, TDocument> ToFindFluent() => Fluent();

    /// <inheritdoc />
    public BsonDocument RenderFilter()
    {
        var registry = BsonSerializer.SerializerRegistry;
        return _filter.Render(new RenderArgs<TDocument>(registry.GetSerializer<TDocument>(), registry));
    }

    private Task<long> CountCoreAsync(CancellationToken cancellationToken)
    {
        var options = _maxTime is { } maxTime ? new CountOptions { MaxTime = maxTime } : null;

        return _session is null
            ? _collection.CountDocumentsAsync(_filter, options, cancellationToken)
            : _collection.CountDocumentsAsync(_session, _filter, options, cancellationToken);
    }

    /// <summary>
    ///     Wraps a typed selector as a field definition.
    /// </summary>
    /// <remarks>
    ///     The driver's sort builders accept a field definition or an
    ///     <c>Expression&lt;Func&lt;TDocument, object&gt;&gt;</c>. Going through
    ///     <see cref="ExpressionFieldDefinition{TDocument,TField}" /> keeps the selector strongly typed and
    ///     lets the driver resolve the whole member path, so sorting on a nested field works.
    /// </remarks>
    private static FieldDefinition<TDocument> ToField<TField>(Expression<Func<TDocument, TField>> field)
        => new ExpressionFieldDefinition<TDocument, TField>(field);

    private SortDefinition<TDocument> Combine(SortDefinition<TDocument> next)
        => _sort is null ? next : Sorts.Combine(_sort, next);

    private IFindFluent<TDocument, TDocument> Fluent(int? skipOverride = null, int? limitOverride = null)
    {
        var fluent = _session is null
            ? _collection.Find(_filter)
            : _collection.Find(_session, _filter);

        var options = fluent.Options;

        if (_sort is not null)
            options.Sort = _sort;

        var skip = skipOverride ?? _skip;
        if (skip is not null)
            options.Skip = skip;

        var limit = limitOverride ?? _limit;
        if (limit is not null)
            options.Limit = limit;

        if (_hint is not null)
            options.Hint = _hint;

        if (_batchSize is not null)
            options.BatchSize = _batchSize;

        if (_maxTime is not null)
            options.MaxTime = _maxTime;

        if (_allowDiskUse is not null)
            options.AllowDiskUse = _allowDiskUse;

        return fluent;
    }

    /// <summary>
    ///     Copies this query, replacing the fields given.
    /// </summary>
    private MongoQuery<TDocument> With(
        FilterDefinition<TDocument>? filter = null,
        SortDefinition<TDocument>? sort = null,
        int? skip = null,
        int? limit = null,
        BsonValue? hint = null,
        int? batchSize = null,
        TimeSpan? maxTime = null,
        bool? allowDiskUse = null,
        IClientSessionHandle? session = null)
        => new(
            _collection,
            _ensureIndexes,
            filter ?? _filter,
            sort ?? _sort,
            skip ?? _skip,
            limit ?? _limit,
            hint ?? _hint,
            batchSize ?? _batchSize,
            maxTime ?? _maxTime,
            allowDiskUse ?? _allowDiskUse,
            session ?? _session);
}
