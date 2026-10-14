using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using SharpMongoRepository.Indexing;

namespace SharpMongoRepository;

/// <summary>
///     Per-document-type repository configuration.
/// </summary>
/// <typeparam name="TDocument">Document type these options apply to.</typeparam>
/// <remarks>
///     <para>
///         Every member has a settable accessor. An earlier version exposed <c>init</c>-only properties, which
///         made the <c>configure</c> delegate on the registration extension impossible to use: nothing could
///         be assigned inside it, so <see cref="OperationTimeout" /> was unreachable.
///     </para>
///     <para>
///         The document's key type is not a parameter here, because nothing in this class depends on it.
///     </para>
/// </remarks>
public class MongoRepositoryOptions<TDocument>
{
    /// <summary>
    ///     Gets or sets the collection name, overriding <see cref="BsonCollectionAttribute" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Setting this makes the attribute unnecessary, which is what lets a type you do not own, a
    ///         shared DTO or a <c>BsonDocument</c> be used as a document. One of the two must be present: a
    ///         type with neither raises <see cref="RepositoryException" /> naming both ways to fix it.
    ///     </para>
    ///     <para>
    ///         It is also the way to point two repositories over the same type at different collections, for
    ///         instance a live collection and an archive.
    ///     </para>
    /// </remarks>
    public string? CollectionName { get; set; }

    /// <summary>
    ///     Gets or sets whether fields present in the stored document but absent from
    ///     <typeparamref name="TDocument" /> are ignored when deserialising. Defaults to
    ///     <see langword="false" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A type with no identifier member needs this, or something equivalent, to be readable at all:
    ///         the server stores an <c>_id</c> on every document, and the default BSON mapping treats an
    ///         unmapped element as a fault, raising
    ///         <c>Element '_id' does not match any field or property of class T</c> on the first read.
    ///     </para>
    ///     <para>
    ///         The ordinary fix is <c>[BsonIgnoreExtraElements]</c> on the type itself, which is explicit
    ///         and visible where the type is declared. Set this instead when the type is not yours to
    ///         decorate: a shared DTO or a package's model.
    ///     </para>
    ///     <para>
    ///         Setting it registers a class map for <typeparamref name="TDocument" /> once per process, and
    ///         does nothing if one is already registered, so an explicit mapping you have set up yourself
    ///         always wins. It is ignored for types the driver maps without a class map, such as
    ///         <c>BsonDocument</c>, which already keeps every element.
    ///     </para>
    ///     <para>
    ///         Be aware of the trade: ignoring extra elements also hides a field that was renamed or
    ///         misspelled, which would otherwise have failed loudly.
    ///     </para>
    /// </remarks>
    public bool IgnoreExtraElements { get; set; }

    /// <summary>
    ///     Gets the indexes to ensure on the collection. Empty by default.
    /// </summary>
    /// <remarks>
    ///     When and whether these are created is controlled by <see cref="IndexCreation" />.
    /// </remarks>
    public IList<MongoIndex<TDocument>> Indexes { get; } = [];

    /// <summary>
    ///     Gets or sets when configured indexes are created. Defaults to
    ///     <see cref="IndexCreationMode.Startup" />.
    /// </summary>
    public IndexCreationMode IndexCreation { get; set; } = IndexCreationMode.Startup;

    /// <summary>
    ///     Gets or sets whether an existing index whose definition no longer matches the configured one is
    ///     dropped and recreated. Defaults to <see langword="false" />.
    /// </summary>
    /// <remarks>
    ///     Leaving this off means a changed definition is reported through the logger and otherwise ignored,
    ///     which is the safe default: rebuilding a large index is an expensive, blocking operation. Turn it
    ///     on only where you are comfortable paying that cost during a deployment.
    /// </remarks>
    public bool RecreateChangedIndexes { get; set; }

    /// <summary>
    ///     Gets or sets the server-side execution cap applied to reads and counts as <c>maxTimeMS</c>.
    ///     Defaults to <see langword="null" />, meaning no cap.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This bounds how long the <i>server</i> spends on a query. It is unrelated to how long the
    ///         client waits: for that, pass a <see cref="CancellationToken" /> to the operation, which every
    ///         asynchronous method accepts.
    ///     </para>
    ///     <para>
    ///         It is deliberately not applied to writes. An earlier version turned this value into a
    ///         <c>WriteConcern</c> with only <c>wtimeout</c> set, which replaced whatever write concern the
    ///         connection string specified, silently downgrading a configured <c>w=majority</c> to the server
    ///         default while achieving nothing, because <c>wtimeout</c> is ignored unless <c>w</c> is greater
    ///         than 1. Set <see cref="WriteConcern" /> explicitly to control durability.
    ///     </para>
    /// </remarks>
    public TimeSpan? OperationTimeout { get; set; }

    /// <summary>
    ///     Gets or sets the write concern for this collection. Defaults to <see langword="null" />, which
    ///     keeps whatever the connection string or client specifies.
    /// </summary>
    /// <example>
    ///     <code>options.WriteConcern = WriteConcern.WMajority.With(journal: true);</code>
    /// </example>
    public WriteConcern? WriteConcern { get; set; }

    /// <summary>
    ///     Gets or sets the read concern for this collection. Defaults to <see langword="null" />, which
    ///     keeps whatever the connection string or client specifies.
    /// </summary>
    public ReadConcern? ReadConcern { get; set; }

    /// <summary>
    ///     Gets or sets the read preference for this collection. Defaults to <see langword="null" />, which
    ///     keeps whatever the connection string or client specifies.
    /// </summary>
    /// <example>
    ///     <code>options.ReadPreference = ReadPreference.SecondaryPreferred;</code>
    /// </example>
    public ReadPreference? ReadPreference { get; set; }

    /// <summary>
    ///     Gets or sets the number of documents the server returns per batch, for reads that stream.
    /// </summary>
    /// <remarks>
    ///     Only worth setting when documents are unusually large or unusually small. A smaller batch caps
    ///     how much a streaming read holds at once; a larger one cuts round-trips on a long scan. Individual
    ///     queries can override it through <see cref="IMongoQuery{TDocument}.WithBatchSize" />.
    /// </remarks>
    public int? BatchSize { get; set; }

    /// <summary>
    ///     Gets or sets the generator used to fill in an empty identifier on insert.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Only consulted by the keyed repository, which knows where the document's key lives. A keyless
    ///         repository leaves identifiers to the driver and the server.
    ///     </para>
    ///     <para>
    ///         When left <see langword="null" />, a generator is chosen from the key type: <c>ObjectId</c>,
    ///         <see cref="Guid" /> and <see cref="string" /> are all handled. For any other key type the
    ///         caller assigns identifiers, and no exception is raised.
    ///     </para>
    ///     <para>
    ///         For <see cref="Guid" /> keys, consider
    ///         <see cref="MongoDB.Bson.Serialization.IdGenerators.CombGuidGenerator" />. A random version-4
    ///         GUID scatters inserts across the whole <c>_id</c> B-tree, dirtying many pages and splitting
    ///         them repeatedly; a COMB GUID embeds a timestamp so inserts land at the right edge of the tree,
    ///         which cuts page splits and keeps the hot working set small.
    ///     </para>
    /// </remarks>
    public IIdGenerator? IdGenerator { get; set; }

    /// <summary>
    ///     Gets or sets a hook to adjust the <see cref="MongoClientSettings" /> built from the connection
    ///     string, for settings with no dedicated property here such as pool sizes or compressors.
    /// </summary>
    /// <remarks>
    ///     Only consulted by the connection-string constructor. Through dependency injection, pass
    ///     <c>configureClient</c> to the registration extension instead, so clients stay shared per
    ///     connection string.
    /// </remarks>
    public Action<MongoClientSettings>? ConfigureClientSettings { get; set; }

    /// <summary>
    ///     Adds one index to <see cref="Indexes" />.
    /// </summary>
    /// <param name="index">Index definition, built with <see cref="MongoIndexes{TDocument}" />.</param>
    /// <returns>The same options instance, so calls can be chained.</returns>
    public MongoRepositoryOptions<TDocument> AddIndex(MongoIndex<TDocument> index)
    {
        ArgumentNullException.ThrowIfNull(index);
        Indexes.Add(index);
        return this;
    }

    /// <summary>
    ///     Adds several indexes to <see cref="Indexes" />.
    /// </summary>
    /// <param name="indexes">Index definitions, built with <see cref="MongoIndexes{TDocument}" />.</param>
    /// <returns>The same options instance, so calls can be chained.</returns>
    public MongoRepositoryOptions<TDocument> AddIndexes(
        params ReadOnlySpan<MongoIndex<TDocument>> indexes)
    {
        foreach (var index in indexes)
            AddIndex(index);

        return this;
    }

    /// <summary>
    ///     Sets <see cref="IgnoreExtraElements" /> and returns these options, for chaining.
    /// </summary>
    /// <returns>The same options instance, so calls can be chained.</returns>
    public MongoRepositoryOptions<TDocument> IgnoringExtraElements()
    {
        IgnoreExtraElements = true;
        return this;
    }

    /// <summary>
    ///     Sets <see cref="CollectionName" /> and returns these options, for chaining.
    /// </summary>
    /// <param name="collectionName">Collection to store documents of this type in.</param>
    /// <returns>The same options instance, so calls can be chained.</returns>
    public MongoRepositoryOptions<TDocument> InCollection(string collectionName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionName);
        CollectionName = collectionName;
        return this;
    }
}

/// <summary>
///     Obsolete. The key type is no longer a parameter of the options.
/// </summary>
/// <typeparam name="TDocument">Document type these options apply to.</typeparam>
/// <typeparam name="TKey">Unused.</typeparam>
[Obsolete("Use MongoRepositoryOptions<TDocument>; the key type was never used here. " +
          "This alias will be removed in 0.2.0.")]
public sealed class MongoRepositoryOptions<TDocument, TKey> : MongoRepositoryOptions<TDocument>
    where TDocument : IDocument<TKey>
    where TKey : notnull;

/// <summary>
///     Controls when a repository's configured indexes are created.
/// </summary>
public enum IndexCreationMode
{
    /// <summary>
    ///     Create indexes once during application start-up, before the first request is served.
    /// </summary>
    /// <remarks>
    ///     This is the default. It keeps the index round-trips off the request path entirely. An earlier
    ///     version listed and created indexes from inside the repository's lazy initialiser, which, combined
    ///     with a scoped registration, meant one <c>listIndexes</c> round-trip per HTTP request.
    /// </remarks>
    Startup = 0,

    /// <summary>
    ///     Create indexes the first time the collection is touched, once per process.
    /// </summary>
    /// <remarks>
    ///     Useful outside a host that runs hosted services. A failed attempt is not cached, so the next
    ///     call retries rather than leaving the repository permanently broken.
    /// </remarks>
    FirstUse = 1,

    /// <summary>
    ///     Never create indexes. Choose this when indexes are managed by migrations or by infrastructure code.
    /// </summary>
    Disabled = 2
}
