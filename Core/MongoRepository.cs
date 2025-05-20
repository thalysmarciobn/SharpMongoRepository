using System.Linq.Expressions;
using MongoDB.Bson;
using MongoDB.Driver;
using SharpMongoRepository.Attributes;
using SharpMongoRepository.Configuration;
using SharpMongoRepository.Exceptions;
using SharpMongoRepository.Interface;

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
///         </list>
///     </para>
///     <example>
///         <para>Typical DI registration:</para>
///         <code>
/// services.AddScoped<IMongoRepository<SampleEntity>
///                 >(provider =>
///                 {
///                 var config = provider.GetRequiredService<IOptions
///                 <MongoConfig>
///                     >().Value;
///                     var options = new MongoRepositoryOptions
///                     <SampleEntity>
///                         {
///                         Indexes =
///                         [
///                         new() {
///                         Keys = Builders
///                         <SampleEntity>
///                             .IndexKeys.Ascending(x => x.Email),
///                             Options = new CreateIndexOptions { Unique = true }
///                             }
///                             ]
///                             };
///                             return new MongoRepository
///                             <SampleEntity>
///                                 (config.ConnectionString, config.DatabaseName, options);
///                                 });
/// </code>
///         <para>Example usage:</para>
///         <code>
/// public class SampleService(IMongoRepository<SampleEntity>
///                 repository)
///                 {
///                 public async Task AddSample(SampleEntity entity)
///                 => await repository.InsertOneAsync(entity);
///                 public async Task<SampleEntity?> GetByEmail(string email)
///                 => await repository.FindOneAsync(x => x.Email == email);
///                 }
/// </code>
///     </example>
/// </remarks>
public sealed class MongoRepository<TDocument> : IMongoRepository<TDocument> where TDocument : IDocument
{
    private readonly string _collectionName;
    private readonly string _connectionString;
    private readonly string _databaseName;

    private readonly Lazy<IMongoClient> _lazyClient;
    private readonly Lazy<IMongoCollection<TDocument>> _lazyCollection;

    private readonly MongoRepositoryOptions<TDocument>? _options;

    /// <summary>
    ///     Initializes a new MongoDB repository instance.
    /// </summary>
    /// <param name="connectionString">MongoDB connection string.</param>
    /// <param name="databaseName">Target database name.</param>
    /// <param name="options">Optional index configuration.</param>
    /// <exception cref="RepositoryException">
    ///     Thrown when collection name is missing or connection fails.
    /// </exception>
    public MongoRepository(string connectionString, string databaseName, MongoRepositoryOptions<TDocument>? options)
    {
        _connectionString = connectionString;
        _databaseName = databaseName;

        _collectionName = GetCollectionName(typeof(TDocument)) ??
                          throw new RepositoryException("Collection name not specified.");

        _lazyClient = new Lazy<IMongoClient>(CreateMongoClient, LazyThreadSafetyMode.ExecutionAndPublication);

        _lazyCollection =
            new Lazy<IMongoCollection<TDocument>>(InitializeCollection, LazyThreadSafetyMode.ExecutionAndPublication);

        _options = options;
    }

    /// <inheritdoc />
    public IFindFluent<TDocument, TDocument> Find(FilterDefinition<TDocument> filter)
    {
        return _lazyCollection.Value.Find(filter);
    }

    /// <inheritdoc />
    public IQueryable<TDocument> AsQueryable()
    {
        return _lazyCollection.Value.AsQueryable();
    }

    /// <inheritdoc />
    public IEnumerable<TDocument> FilterBy(Expression<Func<TDocument, bool>> filterExpression)
    {
        return _lazyCollection.Value.Find(filterExpression).ToEnumerable();
    }

    /// <inheritdoc />
    public IEnumerable<TProjected> FilterBy<TProjected>(
        Expression<Func<TDocument, bool>> filterExpression,
        Expression<Func<TDocument, TProjected>> projectionExpression)
    {
        return _lazyCollection.Value.Find(filterExpression).Project(projectionExpression).ToEnumerable();
    }

    /// <inheritdoc />
    public async Task<IAsyncCursor<TDocument>> AllAsync()
    {
        return await _lazyCollection.Value.FindAsync(Builders<TDocument>.Filter.Empty);
    }

    /// <inheritdoc />
    public TDocument FindOne(Expression<Func<TDocument, bool>> filterExpression)
    {
        return _lazyCollection.Value.Find(filterExpression).FirstOrDefault();
    }

    /// <inheritdoc />
    public async Task<TDocument?> FindOneAsync(Expression<Func<TDocument, bool>> filterExpression)
    {
        return await _lazyCollection.Value.Find(filterExpression).FirstOrDefaultAsync();
    }

    /// <inheritdoc />
    public TDocument FindById(string id)
    {
        var objectId = new ObjectId(id);
        var filter = Builders<TDocument>.Filter.Eq(doc => doc.Id, objectId);
        return _lazyCollection.Value.Find(filter).SingleOrDefault();
    }

    /// <inheritdoc />
    public async Task<TDocument?> FindByIdAsync(string id)
    {
        var objectId = new ObjectId(id);
        var filter = Builders<TDocument>.Filter.Eq(doc => doc.Id, objectId);
        var find = await _lazyCollection.Value.FindAsync(filter);
        return await find.SingleOrDefaultAsync();
    }

    /// <inheritdoc />
    public void InsertOne(TDocument document)
    {
        if (document.Id == ObjectId.Empty)
            document.Id = ObjectId.GenerateNewId();

        _lazyCollection.Value.InsertOne(document);
    }

    /// <inheritdoc />
    public async Task InsertOneAsync(TDocument document)
    {
        await _lazyCollection.Value.InsertOneAsync(document);
    }

    /// <inheritdoc />
    public void InsertMany(ICollection<TDocument> documents)
    {
        _lazyCollection.Value.InsertMany(documents);
    }

    /// <inheritdoc />
    public async Task<long> AsyncCount(Expression<Func<TDocument, bool>> filterExpression)
    {
        var filter = Builders<TDocument>.Filter.Where(filterExpression);
        return await _lazyCollection.Value.CountDocumentsAsync(filter);
    }

    /// <inheritdoc />
    public long Count(Expression<Func<TDocument, bool>> filterExpression)
    {
        var filter = Builders<TDocument>.Filter.Where(filterExpression);
        return _lazyCollection.Value.CountDocuments(filter);
    }

    /// <inheritdoc />
    public async Task InsertManyAsync(ICollection<TDocument> documents)
    {
        await _lazyCollection.Value.InsertManyAsync(documents);
    }

    /// <inheritdoc />
    public void ReplaceOne(TDocument document)
    {
        var filter = Builders<TDocument>.Filter.Eq(doc => doc.Id, document.Id);
        _lazyCollection.Value.FindOneAndReplace(filter, document);
    }

    /// <inheritdoc />
    public async Task<TDocument> ReplaceOneAsync(TDocument document)
    {
        var filter = Builders<TDocument>.Filter.Eq(doc => doc.Id, document.Id);
        return await _lazyCollection.Value.FindOneAndReplaceAsync(filter, document);
    }

    /// <inheritdoc />
    public void DeleteOne(Expression<Func<TDocument, bool>> filterExpression)
    {
        _lazyCollection.Value.FindOneAndDelete(filterExpression);
    }

    /// <inheritdoc />
    public async Task DeleteOneAsync(Expression<Func<TDocument, bool>> filterExpression)
    {
        await _lazyCollection.Value.FindOneAndDeleteAsync(filterExpression);
    }

    /// <inheritdoc />
    public void DeleteById(string id)
    {
        var objectId = new ObjectId(id);
        var filter = Builders<TDocument>.Filter.Eq(doc => doc.Id, objectId);
        _lazyCollection.Value.FindOneAndDelete(filter);
    }

    /// <inheritdoc />
    public async Task DeleteByIdAsync(string id)
    {
        var objectId = new ObjectId(id);
        var filter = Builders<TDocument>.Filter.Eq(doc => doc.Id, objectId);
        await _lazyCollection.Value.FindOneAndDeleteAsync(filter);
    }

    /// <inheritdoc />
    public void DeleteMany(Expression<Func<TDocument, bool>> filterExpression)
    {
        _lazyCollection.Value.DeleteMany(filterExpression);
    }

    /// <inheritdoc />
    public async Task DeleteManyAsync(Expression<Func<TDocument, bool>> filterExpression)
    {
        await _lazyCollection.Value.DeleteManyAsync(filterExpression);
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
            return new MongoClient(_connectionString);
        }
        catch (Exception ex)
        {
            throw new RepositoryException("Failed to create MongoDB client.", ex);
        }
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
    private void CreateIndexes(IMongoCollection<TDocument> collection, MongoRepositoryOptions<TDocument> options)
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