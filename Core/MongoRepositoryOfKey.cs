using System.Linq.Expressions;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using SharpMongoRepository.Internal;

namespace SharpMongoRepository;

/// <summary>
///     Default <see cref="IMongoRepository{TDocument,TKey}" /> implementation, for a document type with a
///     typed primary key.
/// </summary>
/// <typeparam name="TDocument">Document type, which must implement <see cref="IDocument{TKey}" />.</typeparam>
/// <typeparam name="TKey">Type of the document's primary key.</typeparam>
/// <remarks>
///     Adds the key-based operations and identifier generation to
///     <see cref="MongoRepository{TDocument}" />, whose filter-based surface, including
///     <see cref="MongoRepository{TDocument}.Query" />, it inherits unchanged.
/// </remarks>
public sealed class MongoRepository<TDocument, TKey> :
    MongoRepository<TDocument>,
    IMongoRepository<TDocument, TKey>
    where TDocument : IDocument<TKey>
    where TKey : notnull
{
    /// <summary>
    ///     Selector for the primary key, built once per closed generic type.
    /// </summary>
    /// <remarks>
    ///     An expression tree is constructed afresh on each evaluation of a lambda, so calling
    ///     <c>Builders&lt;TDocument&gt;.Filter.Eq(doc =&gt; doc.Id, id)</c> inside each method would allocate
    ///     a new tree per call. Hoisting it into a <c>static readonly</c> field builds it exactly once.
    /// </remarks>
    private static readonly Expression<Func<TDocument, TKey>> IdSelector = document => document.Id;

    /// <summary>
    ///     The primary key field.
    /// </summary>
    /// <remarks>
    ///     Deliberately an expression field definition rather than the string <c>"_id"</c>. A string field
    ///     definition resolves no member, so it would serialise the value with the registry's default
    ///     serializer for <typeparamref name="TKey" />: for a <see cref="Guid" /> that throws
    ///     (<c>GuidSerializer cannot serialize a Guid when GuidRepresentation is Unspecified</c>), and a key
    ///     carrying <c>[BsonRepresentation(BsonType.String)]</c> would be matched against the wrong BSON
    ///     type. Going through the expression lets the driver resolve the member's own serializer.
    /// </remarks>
    private static readonly FieldDefinition<TDocument, TKey> IdField =
        new ExpressionFieldDefinition<TDocument, TKey>(IdSelector);

    /// <inheritdoc cref="MongoRepository{TDocument}(IMongoDatabase, MongoRepositoryOptions{TDocument}, ILogger)" />
    public MongoRepository(
        IMongoDatabase database,
        MongoRepositoryOptions<TDocument>? options = null,
        ILogger? logger = null)
        : base(database, options, logger)
    {
    }

    /// <inheritdoc cref="MongoRepository{TDocument}(string, string, MongoRepositoryOptions{TDocument}, ILogger)" />
    public MongoRepository(
        string connectionString,
        string databaseName,
        MongoRepositoryOptions<TDocument>? options = null,
        ILogger? logger = null)
        : base(connectionString, databaseName, options, logger)
    {
    }

    internal MongoRepository(
        IMongoDatabase database,
        MongoRepositoryOptions<TDocument>? options,
        ILogger? logger,
        MongoIndexEnsurer ensurer)
        : base(database, options, logger, ensurer)
    {
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
    public Task<ReplaceOneResult> ReplaceOneAsync(
        TDocument document,
        bool upsert = false,
        IClientSessionHandle? session = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (upsert)
            AssignIdentifier(document);

        return ReplaceOneCoreAsync(
            Filters.Eq(IdField, document.Id), document, upsert, session, cancellationToken);
    }

    /// <inheritdoc />
    public Task<TDocument?> FindOneAndReplaceAsync(
        TDocument document,
        ReturnDocument returnDocument = ReturnDocument.After,
        bool upsert = false,
        IClientSessionHandle? session = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (upsert)
            AssignIdentifier(document);

        return FindOneAndReplaceCoreAsync(
            Filters.Eq(IdField, document.Id), document, returnDocument, upsert, session,
            cancellationToken);
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
    public IMongoQuery<TDocument> QueryById(TKey id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return QueryCore(Filters.Eq(IdField, id));
    }

    /// <inheritdoc />
    protected override void AssignIdentifier(TDocument document)
        => IdAssigner<TDocument, TKey>.Assign(document, Options.IdGenerator);

    /// <inheritdoc />
    protected override void AssignIdentifiers(IList<TDocument> documents)
        => IdAssigner<TDocument, TKey>.Assign(documents, Options.IdGenerator);
}
