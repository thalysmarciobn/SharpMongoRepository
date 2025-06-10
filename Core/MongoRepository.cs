using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.IdGenerators;
using MongoDB.Driver;
using SharpMongoRepository.Attributes;
using SharpMongoRepository.Configuration;
using SharpMongoRepository.Exceptions;
using SharpMongoRepository.Interface;
using SharpMongoRepository.Serialization;
using System.Linq.Expressions;

namespace SharpMongoRepository;

/// <summary>
///     Provides a complete implementation of MongoDB CRUD operations with index support.
/// </summary>
/// <typeparam name="TDocument">The document type implementing IDocument.</typeparam>
/// <remarks>
///     <para>
///         Features include:
///         <list type="bullet">
///             <item>Automatic ID generation</item>
///             <item>Index management</item>
///             <item>LINQ support</item>
///             <item>Async/Await pattern</item>
///             <item>Projection queries</item>
///             <item>Configurable timeouts</item>
///             <item>Transaction support</item>
///             <item>Null argument validation</item>
///         </list>
///     </para>
/// </remarks>
public sealed class MongoRepository<TDocument, TKey> : IMongoRepository<TDocument, TKey>
    where TDocument : IDocument<TKey>
{
    private readonly string _collectionName;
    private readonly string _connectionString;
    private readonly string _databaseName;

    private readonly Lazy<IMongoClient> _lazyClient;
    private readonly Lazy<IMongoCollection<TDocument>> _lazyCollection;

    private readonly IIdGenerator _idGenerator;
    private readonly MongoRepositoryOptions<TDocument, TKey>? _options;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MongoRepository{TDocument, TKey}"/> class.
    ///     Represents a MongoDB repository for the specified document and key types.
    /// </summary>
    /// <param name="connectionString">The MongoDB connection string used to connect to the database.</param>
    /// <param name="databaseName">The name of the target MongoDB database.</param>
    /// <param name="options">
    ///     Optional repository options, such as index configurations and other settings.
    /// </param>
    /// <param name="idGenerator">
    ///     An optional ID generator for creating new unique identifiers of type <typeparamref name="TKey"/>.
    ///     If not provided, the repository must handle ID assignment accordingly.
    /// </param>
    /// <exception cref="RepositoryException">
    ///     Thrown when the collection name cannot be determined from <typeparamref name="TDocument"/> or if connection setup fails.
    /// </exception>
    public MongoRepository(string connectionString, string databaseName, MongoRepositoryOptions<TDocument, TKey>? options, IIdGenerator? idGenerator = null)
    {
        _connectionString = connectionString;

        _databaseName = databaseName;

        _collectionName = GetCollectionName(typeof(TDocument)) ??
                          throw new RepositoryException("Collection name not specified.");

        _lazyClient = new Lazy<IMongoClient>(CreateMongoClient, LazyThreadSafetyMode.ExecutionAndPublication);

        _lazyCollection =
            new Lazy<IMongoCollection<TDocument>>(InitializeCollection, LazyThreadSafetyMode.ExecutionAndPublication);

        _idGenerator = idGenerator ?? CreateDefaultIdGenerator();

        _options = options;
    }

    /// <summary>
    /// Creates a default <see cref="IIdGenerator"/> implementation based on the type parameter <typeparamref name="TKey"/>.
    /// </summary>
    /// <returns>
    /// An instance of <see cref="IIdGenerator"/> appropriate for generating IDs of type <typeparamref name="TKey"/>.
    /// </returns>
    /// <exception cref="RepositoryException">
    /// Thrown when there is no default <see cref="IIdGenerator"/> implementation available for the specified <typeparamref name="TKey"/> type.
    /// </exception>
    /// <remarks>
    /// This method supports the following ID types:
    /// <list type="bullet">
    ///   <item><description><see cref="ObjectId"/> — returns an <see cref="ObjectIdGenerator"/>.</description></item>
    ///   <item><description><see cref="Guid"/> — returns a <see cref="GuidIDGenerator"/>.</description></item>
    ///   <item><description><see cref="string"/> — returns a <see cref="StringObjectIdGenerator"/>.</description></item>
    /// </list>
    /// If the <typeparamref name="TKey"/> type does not match any supported types, a <see cref="RepositoryException"/> is thrown.
    /// </remarks>
    private IIdGenerator CreateDefaultIdGenerator()
    {
        if (typeof(TKey) == typeof(ObjectId))
            return new ObjectIdGenerator();

        if (typeof(TKey) == typeof(Guid))
            return new GuidIDGenerator();

        if (typeof(TKey) == typeof(string))
            return new StringObjectIdGenerator();

        throw new RepositoryException($"No default IdGenerator available for type {typeof(TKey).Name}.");
    }

    /// <inheritdoc />
    public IFindFluent<TDocument, TDocument> Find(FilterDefinition<TDocument> filter)
    {
        var options = ApplyTimeout();

        return _lazyCollection.Value.Find(filter, options);
    }

    /// <inheritdoc />
    public IQueryable<TDocument> AsQueryable()
    {
        return _lazyCollection.Value.AsQueryable();
    }

    /// <inheritdoc />
    public IEnumerable<TDocument> FilterBy(Expression<Func<TDocument, bool>> filterExpression)
    {
        var options = ApplyTimeout();

        return _lazyCollection.Value.Find(filterExpression, options).ToEnumerable();
    }

    /// <inheritdoc />
    public IEnumerable<TDocument> FilterBy(Expression<Func<TDocument, bool>> filterExpression, IClientSessionHandle clientSessionHandle)
    {
        var options = ApplyTimeout();

        return _lazyCollection.Value.Find(clientSessionHandle, filterExpression, options).ToEnumerable();
    }

    /// <inheritdoc />
    public IEnumerable<TProjected> FilterBy<TProjected>(
        Expression<Func<TDocument, bool>> filterExpression,
        Expression<Func<TDocument, TProjected>> projectionExpression)
    {
        var options = ApplyTimeout();

        return _lazyCollection.Value.Find(filterExpression, options).Project(projectionExpression).ToEnumerable();
    }

    /// <inheritdoc />
    public IEnumerable<TProjected> FilterBy<TProjected>(
        Expression<Func<TDocument, bool>> filterExpression,
        Expression<Func<TDocument, TProjected>> projectionExpression,
        IClientSessionHandle clientSessionHandle)
    {
        var options = ApplyTimeout();

        return _lazyCollection.Value.Find(clientSessionHandle, filterExpression, options).Project(projectionExpression).ToEnumerable();
    }

    /// <inheritdoc />
    public async Task<IAsyncCursor<TDocument>> AllAsync()
    {
        var options = ApplyTimeout<TDocument>();

        return await _lazyCollection.Value.FindAsync(Builders<TDocument>.Filter.Empty, options);
    }

    /// <inheritdoc />
    public async Task<IAsyncCursor<TDocument>> AllAsync(IClientSessionHandle clientSessionHandle)
    {
        var options = ApplyTimeout<TDocument>();

        return await _lazyCollection.Value.FindAsync(clientSessionHandle, Builders<TDocument>.Filter.Empty, options);
    }

    /// <inheritdoc />
    public TDocument FindOne(Expression<Func<TDocument, bool>> filterExpression)
    {
        var options = ApplyTimeout();

        return _lazyCollection.Value.Find(filterExpression, options).FirstOrDefault();
    }

    /// <inheritdoc />
    public TDocument FindOne(Expression<Func<TDocument, bool>> filterExpression, IClientSessionHandle clientSessionHandle)
    {
        var options = ApplyTimeout();

        return _lazyCollection.Value.Find(clientSessionHandle, filterExpression, options).FirstOrDefault();
    }

    /// <inheritdoc />
    public async Task<TDocument?> FindOneAsync(Expression<Func<TDocument, bool>> filterExpression)
    {
        var options = ApplyTimeout();

        return await _lazyCollection.Value.Find(filterExpression, options).FirstOrDefaultAsync();
    }

    /// <inheritdoc />
    public async Task<TDocument?> FindOneAsync(Expression<Func<TDocument, bool>> filterExpression, IClientSessionHandle clientSessionHandle)
    {   
        var options = ApplyTimeout();

        return await _lazyCollection.Value.Find(clientSessionHandle, filterExpression, options).FirstOrDefaultAsync();
    }


    /// <inheritdoc />
    public TDocument? FindById(TKey id, FindOptions? options = null)
    {
        var filter = Builders<TDocument>.Filter.Eq(doc => doc.Id, id);

        var opt = options ?? ApplyTimeout();

        return _lazyCollection.Value.Find(filter, options).SingleOrDefault();
    }

    /// <inheritdoc />
    public TDocument? FindById(TKey id, IClientSessionHandle clientSessionHandle, FindOptions? options = null)
    {
        var filter = Builders<TDocument>.Filter.Eq(doc => doc.Id, id);

        var opt = options ?? ApplyTimeout();

        return _lazyCollection.Value.Find(clientSessionHandle, filter, opt).SingleOrDefault();
    }

    /// <inheritdoc />
    public async Task<TDocument?> FindByIdAsync(TKey id, FindOptions? options = null)
    {
        var filter = Builders<TDocument>.Filter.Eq(doc => doc.Id, id);

        var opt = options ?? ApplyTimeout();

        return await _lazyCollection.Value.Find(filter, opt)
            .FirstOrDefaultAsync();
    }

    /// <inheritdoc />
    public async Task<TDocument?> FindByIdAsync(TKey id, IClientSessionHandle clientSessionHandle, FindOptions? options = null)
    {
        var filter = Builders<TDocument>.Filter.Eq(doc => doc.Id, id);

        var opt = options ?? ApplyTimeout();

        return await _lazyCollection.Value.Find(clientSessionHandle, filter, opt)
            .FirstOrDefaultAsync();
    }

    /// <inheritdoc />
    public void InsertOne(TDocument document, InsertOneOptions? options = null)
    {
        if (_idGenerator.IsEmpty(document.Id))
            document.Id = (TKey)_idGenerator.GenerateId(null, document);

        var writeConcern = ApplyWriteTimeout();

        var collection = _lazyCollection.Value
            .WithWriteConcern(writeConcern);

        collection.InsertOne(document, options);
    }

    /// <inheritdoc />
    public void InsertOne(TDocument document, IClientSessionHandle clientSessionHandle, InsertOneOptions? options = null)
    {
        if (_idGenerator.IsEmpty(document.Id))
            document.Id = (TKey)_idGenerator.GenerateId(null, document);

        var writeConcern = ApplyWriteTimeout();

        var collection = _lazyCollection.Value
            .WithWriteConcern(writeConcern);

        collection.InsertOne(clientSessionHandle, document, options);
    }

    /// <inheritdoc />
    public async Task InsertOneAsync(TDocument document, InsertOneOptions? options = null)
    {
        if (_idGenerator.IsEmpty(document.Id))
            document.Id = (TKey)_idGenerator.GenerateId(null, document);

        var writeConcern = ApplyWriteTimeout();

        var collection = _lazyCollection.Value
            .WithWriteConcern(writeConcern);

        await collection.InsertOneAsync(document, options);
    }

    /// <inheritdoc />
    public async Task InsertOneAsync(TDocument document, IClientSessionHandle clientSessionHandle, InsertOneOptions? options = null)
    {
        if (_idGenerator.IsEmpty(document.Id))
            document.Id = (TKey)_idGenerator.GenerateId(null, document);

        var writeConcern = ApplyWriteTimeout();

        var collection = _lazyCollection.Value
            .WithWriteConcern(writeConcern);

        await collection.InsertOneAsync(clientSessionHandle, document, options);
    }

    /// <inheritdoc />
    public void InsertMany(ICollection<TDocument> documents, InsertManyOptions? options = null)
    {
        var writeConcern = ApplyWriteTimeout();

        _lazyCollection.Value
            .WithWriteConcern(writeConcern)
            .InsertMany(documents, options);
    }

    /// <inheritdoc />
    public void InsertMany(ICollection<TDocument> documents, IClientSessionHandle clientSessionHandle, InsertManyOptions? options = null)
    {
        var writeConcern = ApplyWriteTimeout();

        _lazyCollection.Value
            .WithWriteConcern(writeConcern)
            .InsertMany(clientSessionHandle, documents, options);
    }

    /// <inheritdoc />
    public async Task InsertManyAsync(ICollection<TDocument> documents, InsertManyOptions? options = null)
    {
        var writeConcern = ApplyWriteTimeout();

        await _lazyCollection.Value
            .WithWriteConcern(writeConcern)
            .InsertManyAsync(documents, options);
    }

    /// <inheritdoc />
    public async Task InsertManyAsync(ICollection<TDocument> documents, IClientSessionHandle clientSessionHandle, InsertManyOptions? options = null)
    {
        var writeConcern = ApplyWriteTimeout();

        await _lazyCollection.Value
            .WithWriteConcern(writeConcern)
            .InsertManyAsync(clientSessionHandle, documents, options);
    }

    /// <inheritdoc />
    public async Task<long> AsyncCount(Expression<Func<TDocument, bool>> filterExpression)
    {
        var filter = Builders<TDocument>.Filter.Where(filterExpression);

        var options = new CountOptions { MaxTime = _options?.OperationTimeout };

        return await _lazyCollection.Value
            .CountDocumentsAsync(filter, options);
    }

    /// <inheritdoc />
    public async Task<long> AsyncCount(Expression<Func<TDocument, bool>> filterExpression, IClientSessionHandle clientSessionHandle)
    {
        var filter = Builders<TDocument>.Filter.Where(filterExpression);

        var options = new CountOptions { MaxTime = _options?.OperationTimeout };

        return await _lazyCollection.Value
            .CountDocumentsAsync(clientSessionHandle, filter, options);
    }

    /// <inheritdoc />
    public long Count(Expression<Func<TDocument, bool>> filterExpression)
    {
        var filter = Builders<TDocument>.Filter.Where(filterExpression);

        var options = new CountOptions { MaxTime = _options?.OperationTimeout };

        return _lazyCollection.Value
            .CountDocuments(filter, options);
    }

    /// <inheritdoc />
    public long Count(Expression<Func<TDocument, bool>> filterExpression, IClientSessionHandle clientSessionHandle)
    {
        var filter = Builders<TDocument>.Filter.Where(filterExpression);

        var options = new CountOptions { MaxTime = _options?.OperationTimeout };

        return _lazyCollection.Value
            .CountDocuments(clientSessionHandle, filter, options);
    }

    /// <inheritdoc />
    public TDocument FindOneAndReplace(TDocument document, FindOneAndReplaceOptions<TDocument>? options = null)
    {
        var filter = Builders<TDocument>.Filter.Eq(doc => doc.Id, document.Id);

        return _lazyCollection.Value.FindOneAndReplace(filter, document, options);
    }

    /// <inheritdoc />
    public TDocument FindOneAndReplace(TDocument document, IClientSessionHandle clientSessionHandle, FindOneAndReplaceOptions<TDocument>? options = null)
    {
        var filter = Builders<TDocument>.Filter.Eq(doc => doc.Id, document.Id);

        return _lazyCollection.Value.FindOneAndReplace(clientSessionHandle, filter, document, options);
    }

    /// <inheritdoc />
    public async Task<TDocument> FindOneAndReplaceAsync(TDocument document)
    {
        var filter = Builders<TDocument>.Filter.Eq(doc => doc.Id, document.Id);

        return await _lazyCollection.Value.FindOneAndReplaceAsync(filter, document);
    }

    /// <inheritdoc />
    public async Task<TDocument> FindOneAndReplaceAsync(TDocument document, IClientSessionHandle clientSessionHandle)
    {
        var filter = Builders<TDocument>.Filter.Eq(doc => doc.Id, document.Id);

        return await _lazyCollection.Value.FindOneAndReplaceAsync(clientSessionHandle, filter, document);
    }

    /// <inheritdoc />
    public async Task<TDocument> FindOneAndReplaceAsync(TDocument document, FindOneAndReplaceOptions<TDocument> options)
    {
        var filter = Builders<TDocument>.Filter.Eq(doc => doc.Id, document.Id);

        return await _lazyCollection.Value.FindOneAndReplaceAsync(filter, document, options);
    }

    /// <inheritdoc />
    public async Task<TDocument> FindOneAndReplaceAsync(TDocument document, IClientSessionHandle clientSessionHandler, FindOneAndReplaceOptions<TDocument> options)
    {
        var filter = Builders<TDocument>.Filter.Eq(doc => doc.Id, document.Id);

        return await _lazyCollection.Value.FindOneAndReplaceAsync(clientSessionHandler, filter, document, options);
    }

    /// <inheritdoc />
    public void DeleteOne(Expression<Func<TDocument, bool>> filterExpression, FindOneAndDeleteOptions<TDocument>? options = null)
    {
        _lazyCollection.Value.FindOneAndDelete(filterExpression, options);
    }

    /// <inheritdoc />
    public void DeleteOne(Expression<Func<TDocument, bool>> filterExpression, IClientSessionHandle clientSessionHandle, FindOneAndDeleteOptions<TDocument>? options = null)
    {
        _lazyCollection.Value.FindOneAndDelete(clientSessionHandle, filterExpression, options);
    }

    /// <inheritdoc />
    public async Task DeleteOneAsync(Expression<Func<TDocument, bool>> filterExpression, FindOneAndDeleteOptions<TDocument>? options = null)
    {
        await _lazyCollection.Value.FindOneAndDeleteAsync(filterExpression, options);
    }

    /// <inheritdoc />
    public async Task DeleteOneAsync(Expression<Func<TDocument, bool>> filterExpression, IClientSessionHandle clientSessionHandle, FindOneAndDeleteOptions<TDocument>? options = null)
    {
        await _lazyCollection.Value.FindOneAndDeleteAsync(clientSessionHandle, filterExpression, options);
    }

    /// <inheritdoc />
    public void DeleteById(TKey id, FindOneAndDeleteOptions<TDocument>? options = null)
    {
        var filter = Builders<TDocument>.Filter.Eq(doc => doc.Id, id);

        _lazyCollection.Value.FindOneAndDelete(filter, options);
    }

    /// <inheritdoc />
    public void DeleteById(TKey id, IClientSessionHandle clientSessionHandle, FindOneAndDeleteOptions<TDocument>? options = null)
    {
        var filter = Builders<TDocument>.Filter.Eq(doc => doc.Id, id);

        _lazyCollection.Value.FindOneAndDelete(clientSessionHandle, filter, options);
    }

    /// <inheritdoc />
    public async Task DeleteByIdAsync(TKey id, FindOneAndDeleteOptions<TDocument>? options = null)
    {
        var filter = Builders<TDocument>.Filter.Eq(doc => doc.Id, id);

        await _lazyCollection.Value.FindOneAndDeleteAsync(filter, options);
    }

    /// <inheritdoc />
    public async Task DeleteByIdAsync(TKey id, IClientSessionHandle clientSessionHandle, FindOneAndDeleteOptions<TDocument>? options = null)
    {
        var filter = Builders<TDocument>.Filter.Eq(doc => doc.Id, id);

        await _lazyCollection.Value.FindOneAndDeleteAsync(clientSessionHandle, filter, options);
    }

    /// <inheritdoc />
    public void DeleteMany(Expression<Func<TDocument, bool>> filterExpression)
    {
        _lazyCollection.Value.DeleteMany(filterExpression);
    }

    /// <inheritdoc />
    public void DeleteMany(Expression<Func<TDocument, bool>> filterExpression, IClientSessionHandle clientSessionHandle)
    {
        _lazyCollection.Value.DeleteMany(clientSessionHandle, filterExpression);
    }

    /// <inheritdoc />
    public async Task DeleteManyAsync(Expression<Func<TDocument, bool>> filterExpression)
    {
        await _lazyCollection.Value.DeleteManyAsync(filterExpression);
    }

    /// <inheritdoc />
    public async Task DeleteManyAsync(Expression<Func<TDocument, bool>> filterExpression, IClientSessionHandle clientSessionHandle)
    {
        await _lazyCollection.Value.DeleteManyAsync(clientSessionHandle, filterExpression);
    }

    /// <inheritdoc />
    public async Task<TResult> WithTransactionAsync<TResult>(
        Func<IClientSessionHandle, Task<TResult>> transactionBody)
    {
        using var session = await _lazyClient.Value.StartSessionAsync();

        session.StartTransaction();

        try
        {
            var result = await transactionBody(session);

            await session.CommitTransactionAsync();

            return result;
        }
        catch
        {
            await session.AbortTransactionAsync();
            throw;
        }
    }

    /// <summary>
    ///     Creates and returns a new MongoDB client instance using the configured connection string.
    /// </summary>
    /// <returns>A new instance of <see cref="IMongoClient" />.</returns>
    /// <exception cref="RepositoryException">
    ///     Thrown when the MongoDB client cannot be created. The exception contains:
    ///     <list type="bullet">
    ///         <item>
    ///             <description>Original exception as inner exception</description>
    ///         </item>
    ///         <item>
    ///             <description>Detailed error message about the failure</description>
    ///         </item>
    ///     </list>
    /// </exception>
    /// <remarks>
    ///     This method handles the following scenarios:
    ///     <list type="bullet">
    ///         <item>
    ///             <description>Valid connection string format</description>
    ///         </item>
    ///         <item>
    ///             <description>Network connectivity to MongoDB server</description>
    ///         </item>
    ///         <item>
    ///             <description>Authentication if required</description>
    ///         </item>
    ///     </list>
    ///     The client instance is configured with default settings from the connection string.
    /// </remarks>
    private IMongoClient CreateMongoClient()
    {
        try
        {
            var settings = MongoClientSettings.FromConnectionString(_connectionString);

            if (_options?.OperationTimeout is not null)
            {
                settings.ConnectTimeout = _options.OperationTimeout.Value;
                settings.ServerSelectionTimeout = _options.OperationTimeout.Value;
                settings.SocketTimeout = _options.OperationTimeout.Value;
            }

            return new MongoClient(settings);
        }
        catch (Exception ex)
        {
            throw new RepositoryException("Failed to create MongoDB client.", ex);
        }
    }

    /// <summary>
    /// Applies the configured operation timeout to a generic <see cref="FindOptions"/> instance.
    /// </summary>
    /// <param name="options">
    /// Optional existing <see cref="FindOptions"/> instance to apply the timeout to.
    /// If null, creates a new instance.
    /// </param>
    /// <returns>
    /// The <see cref="FindOptions"/> instance with the <see cref="FindOptions.MaxTime"/> 
    /// set to the configured operation timeout.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This method ensures all find operations respect the global timeout configured in 
    /// <see cref="MongoRepositoryOptions{TDocument}.OperationTimeout"/>.
    /// </para>
    /// <para>
    /// When no timeout is configured in the repository options, the <see cref="FindOptions.MaxTime"/> 
    /// will remain null, resulting in no timeout enforcement by the MongoDB driver.
    /// </para>
    /// <example>
    /// Example usage:
    /// <code>
    /// var options = ApplyTimeout();
    /// var cursor = await collection.FindAsync(filter, options);
    /// </code>
    /// </example>
    /// </remarks>
    private FindOptions ApplyTimeout(FindOptions? options = null)
    {
        options ??= new FindOptions();

        options.MaxTime = _options?.OperationTimeout;

        return options;
    }

    /// <summary>
    /// Applies the configured operation timeout to a document-specific <see cref="FindOptions{TDocument}"/> instance.
    /// </summary>
    /// <typeparam name="TDocument">The type of the MongoDB document.</typeparam>
    /// <param name="options">
    /// Optional existing <see cref="FindOptions{TDocument}"/> instance to apply the timeout to.
    /// If null, creates a new instance.
    /// </param>
    /// <returns>
    /// The <see cref="FindOptions{TDocument}"/> instance with the <see cref="FindOptions{TDocument}.MaxTime"/> 
    /// set to the configured operation timeout.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This generic version provides type-safe options configuration for document-specific queries.
    /// </para>
    /// <para>
    /// The timeout behavior is identical to the non-generic <see cref="ApplyTimeout(FindOptions)"/> version.
    /// </para>
    /// <example>
    /// Example usage with projection:
    /// <code>
    /// var options = ApplyTimeout&lt;User&gt;();
    /// var cursor = await collection.FindAsync(
    ///     filter, 
    ///     options.Projection = Builders&lt;User&gt;.Projection.Include(x => x.Email));
    /// </code>
    /// </example>
    /// </remarks>
    private FindOptions<TDocument> ApplyTimeout<TDocument>(FindOptions<TDocument>? options = null)
    {
        options ??= new FindOptions<TDocument>();

        options.MaxTime = _options?.OperationTimeout;

        return options;
    }

    /// <summary>
    /// Applies the configured operation timeout to a <see cref="WriteConcern"/> instance.
    /// </summary>
    /// <returns>
    /// A <see cref="WriteConcern"/> with the write timeout set to the configured operation timeout
    /// if available; otherwise, returns <see cref="WriteConcern.Acknowledged"/>.
    /// </returns>
    /// <remarks>
    /// This method ensures that write operations respect a configured timeout to avoid hanging
    /// indefinitely in case of slow or unresponsive database operations.
    /// </remarks>
    private WriteConcern ApplyWriteTimeout()
    {
        return _options?.OperationTimeout != null
            ? new WriteConcern(wTimeout: _options.OperationTimeout.Value)
            : WriteConcern.Acknowledged;
    }

    /// <summary>
    ///     Initializes and returns a MongoDB collection instance, optionally creating indexes if configured.
    /// </summary>
    /// <returns>An <see cref="IMongoCollection{TDocument}" /> instance for the specified document type.</returns>
    /// <exception cref="RepositoryException">
    ///     Thrown when:
    ///     <list type="bullet">
    ///         <item>
    ///             <description>The collection cannot be accessed</description>
    ///         </item>
    ///         <item>
    ///             <description>Index creation fails</description>
    ///         </item>
    ///     </list>
    ///     The exception contains the original error details.
    /// </exception>
    /// <remarks>
    ///     <para>
    ///         This method performs the following operations:
    ///         <list type="number">
    ///             <item>
    ///                 <description>Gets the database instance from the MongoDB client</description>
    ///             </item>
    ///             <item>
    ///                 <description>Retrieves the specified collection</description>
    ///             </item>
    ///             <item>
    ///                 <description>Creates indexes if <see cref="_options" /> contains index definitions</description>
    ///             </item>
    ///         </list>
    ///     </para>
    ///     <para>
    ///         The collection name is determined by the <see cref="BsonCollectionAttribute" /> on the document type.
    ///     </para>
    ///     <para>
    ///         Index creation is skipped if:
    ///         <list type="bullet">
    ///             <item>
    ///                 <description>No options are provided (<see cref="_options" /> is null)</description>
    ///             </item>
    ///             <item>
    ///                 <description>The indexes already exist in the collection</description>
    ///             </item>
    ///         </list>
    ///     </para>
    /// </remarks>
    private IMongoCollection<TDocument> InitializeCollection()
    {
        try
        {
            var collection = _lazyClient.Value.GetDatabase(_databaseName).GetCollection<TDocument>(_collectionName) ??
                             throw new RepositoryException($"Failed to get collection '{_collectionName}'.");

            if (_options is not null) CreateIndexes(collection, _options);

            return collection;
        }
        catch (Exception ex)
        {
            throw new RepositoryException($"Failed to initialize collection '{_collectionName}'.", ex);
        }
    }

    /// <summary>
    ///     Gets the collection name from the BsonCollectionAttribute.
    /// </summary>
    /// <param name="documentType">The type of the document.</param>
    /// <returns>The collection name if specified, otherwise null.</returns>
    private static string? GetCollectionName(Type documentType)
    {
        var attributes = documentType.GetCustomAttributes(typeof(BsonCollectionAttribute), true);

        var bsonCollectionAttribute = attributes.FirstOrDefault() as BsonCollectionAttribute;

        return bsonCollectionAttribute?.CollectionName;
    }

    /// <summary>
    ///     Creates indexes specified in the repository options.
    /// </summary>
    /// <exception cref="RepositoryException">
    ///     Thrown when an index name is not provided.
    /// </exception>
    private void CreateIndexes(IMongoCollection<TDocument> collection, MongoRepositoryOptions<TDocument, TKey> options)
    {
        var existingIndexNames = collection.Indexes.List()
            .ToList()
            .Select(indexInfo => indexInfo["name"].AsString)
            .ToHashSet();

        foreach (var indexModel in from index in options.Indexes
                 let indexName = index.Options.Name ?? throw new RepositoryException("Index name must be provided.")
                 where !existingIndexNames.Contains(indexName)
                 select new CreateIndexModel<TDocument>(index.Keys, index.Options))
            collection.Indexes.CreateOne(indexModel);
    }
}