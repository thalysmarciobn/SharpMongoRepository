using MongoRepository.Attributes;
using MongoRepository.Configuration;
using MongoRepository.Exceptions;
using MongoRepository.Interface;
using System.Linq.Expressions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoRepository;

/// <summary>
/// Provides a complete implementation of MongoDB CRUD operations with index support.
/// </summary>
/// <typeparam name="TDocument">The document type implementing IDocument.</typeparam>
/// <remarks>
/// <para>
/// Features include:
/// <list type="bullet">
/// <item>Automatic ID generation</item>
/// <item>Index management</item>
/// <item>LINQ support</item>
/// <item>Async/Await pattern</item>
/// <item>Projection queries</item>
/// </list>
/// </para>
/// 
/// <example>
/// <para>Typical DI registration:</para>
/// <code>
/// services.AddScoped<IMongoRepository<SampleEntity>>(provider => 
/// {
///     var config = provider.GetRequiredService<IOptions<MongoConfig>>().Value;
///     var options = new MongoRepositoryOptions<SampleEntity>
///     {
///         Indexes = 
///         [
///             new() { 
///                 Keys = Builders<SampleEntity>.IndexKeys.Ascending(x => x.Email),
///                 Options = new CreateIndexOptions { Unique = true }
///             }
///         ]
///     };
///     return new MongoRepository<SampleEntity>(config.ConnectionString, config.DatabaseName, options);
/// });
/// </code>
/// 
/// <para>Example usage:</para>
/// <code>
/// public class SampleService(IMongoRepository<SampleEntity> repository)
/// {
///     public async Task AddSample(SampleEntity entity) 
///         => await repository.InsertOneAsync(entity);
///     
///     public async Task<SampleEntity?> GetByEmail(string email)
///         => await repository.FindOneAsync(x => x.Email == email);
/// }
/// </code>
/// </example>
/// </remarks>
public sealed class MongoRepository<TDocument> : IMongoRepository<TDocument> where TDocument : IDocument
{
    private readonly IMongoCollection<TDocument> _collection;

    /// <summary>
    /// Initializes a new MongoDB repository instance.
    /// </summary>
    /// <param name="connectionString">MongoDB connection string.</param>
    /// <param name="databaseName">Target database name.</param>
    /// <param name="options">Optional index configuration.</param>
    /// <exception cref="MongoRepositoryException">
    /// Thrown when collection name is missing or connection fails.
    /// </exception>
    public MongoRepository(string connectionString, string databaseName, MongoRepositoryOptions<TDocument>? options)
    {
        var database = new MongoClient(connectionString).GetDatabase(databaseName);
        var collectionName = GetCollectionName(typeof(TDocument)) ?? throw new MongoRepositoryException("Collection name not specified.");
        _collection = database.GetCollection<TDocument>(collectionName) ??
                      throw new MongoRepositoryException("Failed to establish connection.");
        
        if (options is not null)
        {
            CreateIndexes(options);
        }
    }

    /// <inheritdoc/>
    public IFindFluent<TDocument, TDocument> Find(FilterDefinition<TDocument> filter)
    {
        return _collection.Find(filter);
    }

    /// <inheritdoc/>
    public IQueryable<TDocument> AsQueryable()
    {
        return _collection.AsQueryable();
    }

    /// <inheritdoc/>
    public IEnumerable<TDocument> FilterBy(Expression<Func<TDocument, bool>> filterExpression)
    {
        return _collection.Find(filterExpression).ToEnumerable();
    }

    /// <inheritdoc/>
    public IEnumerable<TProjected> FilterBy<TProjected>(
        Expression<Func<TDocument, bool>> filterExpression,
        Expression<Func<TDocument, TProjected>> projectionExpression)
    {
        return _collection.Find(filterExpression).Project(projectionExpression).ToEnumerable();
    }

    /// <inheritdoc/>
    public TDocument FindOne(Expression<Func<TDocument, bool>> filterExpression)
    {
        return _collection.Find(filterExpression).FirstOrDefault();
    }

    /// <inheritdoc/>
    public async Task<TDocument?> FindOneAsync(Expression<Func<TDocument, bool>> filterExpression)
    {
        return await _collection.Find(filterExpression).FirstOrDefaultAsync();
    }

    /// <inheritdoc/>
    public TDocument FindById(string id)
    {
        var objectId = new ObjectId(id);
        var filter = Builders<TDocument>.Filter.Eq(doc => doc.Id, objectId);
        return _collection.Find(filter).SingleOrDefault();
    }

    /// <inheritdoc/>
    public async Task<TDocument?> FindByIdAsync(string id)
    {
        var objectId = new ObjectId(id);
        var filter = Builders<TDocument>.Filter.Eq(doc => doc.Id, objectId);
        var find = await _collection.FindAsync(filter);
        return await find.SingleOrDefaultAsync();
    }

    /// <inheritdoc/>
    public void InsertOne(TDocument document)
    {
        if (document.Id == ObjectId.Empty)
            document.Id = ObjectId.GenerateNewId();

        _collection.InsertOne(document);
    }

    /// <inheritdoc/>
    public async Task InsertOneAsync(TDocument document)
    {
        await _collection.InsertOneAsync(document);
    }

    /// <inheritdoc/>
    public void InsertMany(ICollection<TDocument> documents)
    {
        _collection.InsertMany(documents);
    }

    /// <inheritdoc/>
    public async Task<long> AsyncCount(Expression<Func<TDocument, bool>> filterExpression)
    {
        var filter = Builders<TDocument>.Filter.Where(filterExpression);
        return await _collection.CountDocumentsAsync(filter);
    }

    /// <inheritdoc/>
    public long Count(Expression<Func<TDocument, bool>> filterExpression)
    {
        var filter = Builders<TDocument>.Filter.Where(filterExpression);
        return _collection.CountDocuments(filter);
    }

    /// <inheritdoc/>
    public async Task InsertManyAsync(ICollection<TDocument> documents)
    {
        await _collection.InsertManyAsync(documents);
    }

    /// <inheritdoc/>
    public void ReplaceOne(TDocument document)
    {
        var filter = Builders<TDocument>.Filter.Eq(doc => doc.Id, document.Id);
        _collection.FindOneAndReplace(filter, document);
    }

    /// <inheritdoc/>
    public async Task ReplaceOneAsync(TDocument document)
    {
        var filter = Builders<TDocument>.Filter.Eq(doc => doc.Id, document.Id);
        await _collection.FindOneAndReplaceAsync(filter, document);
    }

    /// <inheritdoc/>
    public void DeleteOne(Expression<Func<TDocument, bool>> filterExpression)
    {
        _collection.FindOneAndDelete(filterExpression);
    }

    /// <inheritdoc/>
    public async Task DeleteOneAsync(Expression<Func<TDocument, bool>> filterExpression)
    {
        await _collection.FindOneAndDeleteAsync(filterExpression);
    }

    /// <inheritdoc/>
    public void DeleteById(string id)
    {
        var objectId = new ObjectId(id);
        var filter = Builders<TDocument>.Filter.Eq(doc => doc.Id, objectId);
        _collection.FindOneAndDelete(filter);
    }

    /// <inheritdoc/>
    public async Task DeleteByIdAsync(string id)
    {
        var objectId = new ObjectId(id);
        var filter = Builders<TDocument>.Filter.Eq(doc => doc.Id, objectId);
        await _collection.FindOneAndDeleteAsync(filter);
    }

    /// <inheritdoc/>
    public void DeleteMany(Expression<Func<TDocument, bool>> filterExpression)
    {
        _collection.DeleteMany(filterExpression);
    }

    /// <inheritdoc/>
    public async Task DeleteManyAsync(Expression<Func<TDocument, bool>> filterExpression)
    {
        await _collection.DeleteManyAsync(filterExpression);
    }

    /// <summary>
    /// Gets the collection name from the BsonCollectionAttribute.
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
    /// Creates indexes specified in the repository options.
    /// </summary>
    /// <exception cref="MongoRepositoryException">
    /// Thrown when an index name is not provided.
    /// </exception>
    private void CreateIndexes(MongoRepositoryOptions<TDocument> options)
    {
        var existingIndexNames = _collection.Indexes.List()
            .ToList()
            .Select(indexInfo => indexInfo["name"].AsString)
            .ToHashSet();

        foreach (var indexModel in from index in options.Indexes let indexName = index.Options.Name ?? throw new MongoRepositoryException("Index name must be provided.") where !existingIndexNames.Contains(indexName) select new CreateIndexModel<TDocument>(index.Keys, index.Options))
        {
            _collection.Indexes.CreateOne(indexModel);
        }
    }
}