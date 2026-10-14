using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using SharpMongoRepository.Indexing;

namespace SharpMongoRepository;

/// <summary>
///     Per-document-type repository configuration.
/// </summary>
/// <typeparam name="TDocument">Document type these options apply to.</typeparam>
/// <typeparam name="TKey">Type of the document's primary key.</typeparam>
/// <remarks>
///     Every member has a settable accessor. The previous version exposed <c>init</c>-only properties, which
///     made the <c>configureOptions</c> delegate on the registration extension impossible to use: nothing
///     could be assigned inside it, so <see cref="OperationTimeout" /> was unreachable.
/// </remarks>
public sealed class MongoRepositoryOptions<TDocument, TKey>
    where TDocument : IDocument<TKey>
    where TKey : notnull
{
    /// <summary>
    ///     Gets the indexes to ensure on the collection. Empty by default.
    /// </summary>
    /// <remarks>
    ///     When and whether these are created is controlled by <see cref="IndexCreation" />.
    /// </remarks>
    public IList<MongoIndex<TDocument, TKey>> Indexes { get; } = [];

    /// <summary>
    ///     Gets or sets when configured indexes are created. Defaults to
    ///     <see cref="IndexCreationMode.Startup" />.
    /// </summary>
    public IndexCreationMode IndexCreation { get; set; } = IndexCreationMode.Startup;

    /// <summary>
    ///     Adds one index to <see cref="Indexes" />.
    /// </summary>
    /// <param name="index">Index definition, built with <see cref="MongoIndexes{TDocument,TKey}" />.</param>
    /// <returns>The same options instance, so calls can be chained.</returns>
    public MongoRepositoryOptions<TDocument, TKey> AddIndex(MongoIndex<TDocument, TKey> index)
    {
        ArgumentNullException.ThrowIfNull(index);
        Indexes.Add(index);
        return this;
    }

    /// <summary>
    ///     Adds several indexes to <see cref="Indexes" />.
    /// </summary>
    /// <param name="indexes">Index definitions, built with <see cref="MongoIndexes{TDocument,TKey}" />.</param>
    /// <returns>The same options instance, so calls can be chained.</returns>
    public MongoRepositoryOptions<TDocument, TKey> AddIndexes(
        params ReadOnlySpan<MongoIndex<TDocument, TKey>> indexes)
    {
        foreach (var index in indexes)
            AddIndex(index);

        return this;
    }

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
    ///         asynchronous method on <see cref="IMongoRepository{TDocument,TKey}" /> accepts.
    ///     </para>
    ///     <para>
    ///         It is deliberately not applied to writes. The previous version turned this value into a
    ///         <c>WriteConcern</c> with only <c>wtimeout</c> set, which replaced whatever write concern the
    ///         connection string specified, silently downgrading a configured <c>w=majority</c> to the server
    ///         default while achieving nothing, because <c>wtimeout</c> is ignored unless <c>w</c> is greater
    ///         than 1. Set <see cref="WriteConcern" /> explicitly if you want to control durability.
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
    ///     Gets or sets the generator used to fill in an empty <see cref="IDocument{TKey}.Id" /> on insert.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         When left <see langword="null" />, the generator registered on the BSON class map is used, and
    ///         failing that one is chosen from <typeparamref name="TKey" />: <c>ObjectId</c>,
    ///         <see cref="Guid" /> and <see cref="string" /> are all handled. For any other key type the
    ///         caller is responsible for assigning identifiers, and no exception is raised until a document
    ///         with an empty key is actually inserted.
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
    ///     Clients are pooled per resulting settings object, so two registrations that configure settings
    ///     identically share one <see cref="IMongoClient" /> and therefore one connection pool.
    /// </remarks>
    public Action<MongoClientSettings>? ConfigureClientSettings { get; set; }
}

/// <summary>
///     Controls when a repository's configured indexes are created.
/// </summary>
public enum IndexCreationMode
{
    /// <summary>
    ///     Create indexes once during application start-up, before the first request is served.
    /// </summary>
    /// <remarks>
    ///     This is the default. It keeps the index round-trips off the request path entirely. The previous
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
