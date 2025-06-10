using System.Linq.Expressions;
using MongoDB.Driver;

namespace SharpMongoRepository.Interface;

/// <summary>
/// Represents a generic repository interface for MongoDB operations.
/// </summary>
/// <typeparam name="TDocument">The document type that implements <see cref="IDocument{TKey}"/>.</typeparam>
/// <typeparam name="TKey">The type of the document's primary key.</typeparam>
/// <remarks>
/// This interface provides synchronous and asynchronous CRUD operations for MongoDB,
/// with support for filtering, projection, counting, and transaction execution.
/// </remarks>
public interface IMongoRepository<TDocument, TKey> where TDocument : IDocument<TKey>
{
    /// <summary>
    /// Finds documents matching the specified MongoDB filter definition.
    /// </summary>
    /// <param name="filter">The MongoDB filter definition.</param>
    /// <returns>An <see cref="IFindFluent{TDocument, TDocument}"/> for additional query operations.</returns>
    IFindFluent<TDocument, TDocument> Find(FilterDefinition<TDocument> filter);

    /// <summary>
    /// Provides LINQ query capabilities for the document collection.
    /// </summary>
    /// <returns>An <see cref="IQueryable{TDocument}"/> for LINQ queries.</returns>
    IQueryable<TDocument?> AsQueryable();

    /// <summary>
    /// Filters documents using the specified predicate expression.
    /// </summary>
    /// <param name="filterExpression">LINQ expression used to filter documents.</param>
    /// <returns>A collection of documents matching the filter.</returns>
    IEnumerable<TDocument?> FilterBy(Expression<Func<TDocument, bool>> filterExpression);

    /// <summary>
    /// Filters documents using the specified predicate expression within a transaction session.
    /// </summary>
    /// <param name="filterExpression">LINQ expression used to filter documents.</param>
    /// <param name="clientSessionHandle">The MongoDB client session handle.</param>
    /// <returns>A collection of documents matching the filter.</returns>
    IEnumerable<TDocument?> FilterBy(Expression<Func<TDocument, bool>> filterExpression, IClientSessionHandle clientSessionHandle);

    /// <summary>
    /// Filters and projects documents using the provided expressions.
    /// </summary>
    /// <typeparam name="TProjected">The type of the projected result.</typeparam>
    /// <param name="filterExpression">LINQ expression used to filter documents.</param>
    /// <param name="projectionExpression">LINQ expression used to project documents.</param>
    /// <returns>A collection of projected results.</returns>
    IEnumerable<TProjected> FilterBy<TProjected>(
        Expression<Func<TDocument, bool>> filterExpression,
        Expression<Func<TDocument, TProjected>> projectionExpression);

    /// <summary>
    /// Filters and projects documents using the provided expressions within a transaction session.
    /// </summary>
    /// <typeparam name="TProjected">The type of the projected result.</typeparam>
    /// <param name="filterExpression">LINQ expression used to filter documents.</param>
    /// <param name="projectionExpression">LINQ expression used to project documents.</param>
    /// <param name="clientSessionHandle">The MongoDB client session handle.</param>
    /// <returns>A collection of projected results.</returns>
    IEnumerable<TProjected> FilterBy<TProjected>(
        Expression<Func<TDocument, bool>> filterExpression,
        Expression<Func<TDocument, TProjected>> projectionExpression,
        IClientSessionHandle clientSessionHandle);

    /// <summary>
    /// Asynchronously retrieves all documents from the collection.
    /// </summary>
    /// <returns>A task that returns an async cursor to iterate over the documents.</returns>
    Task<IAsyncCursor<TDocument>> AllAsync();

    /// <summary>
    /// Asynchronously retrieves all documents from the collection within a transaction session.
    /// </summary>
    /// <param name="clientSessionHandle">The MongoDB client session handle.</param>
    /// <returns>A task that returns an async cursor to iterate over the documents.</returns>
    Task<IAsyncCursor<TDocument>> AllAsync(IClientSessionHandle clientSessionHandle);

    /// <summary>
    /// Finds a single document matching the specified expression.
    /// </summary>
    /// <param name="filterExpression">LINQ expression to filter the document.</param>
    /// <returns>The matching document, or null if not found.</returns>
    TDocument? FindOne(Expression<Func<TDocument, bool>> filterExpression);

    /// <summary>
    /// Finds a single document matching the specified expression within a transaction session.
    /// </summary>
    /// <param name="filterExpression">LINQ expression to filter the document.</param>
    /// <param name="clientSessionHandle">The MongoDB client session handle.</param>
    /// <returns>The matching document, or null if not found.</returns>
    TDocument? FindOne(Expression<Func<TDocument, bool>> filterExpression, IClientSessionHandle clientSessionHandle);

    /// <summary>
    /// Asynchronously finds a single document matching the specified expression.
    /// </summary>
    /// <param name="filterExpression">LINQ expression to filter the document.</param>
    /// <returns>A task that returns the matching document or null.</returns>
    Task<TDocument?> FindOneAsync(Expression<Func<TDocument, bool>> filterExpression);

    /// <summary>
    /// Asynchronously finds a single document matching the specified expression within a transaction session.
    /// </summary>
    /// <param name="filterExpression">LINQ expression to filter the document.</param>
    /// <param name="clientSessionHandle">The MongoDB client session handle.</param>
    /// <returns>A task that returns the matching document or null.</returns>
    Task<TDocument?> FindOneAsync(Expression<Func<TDocument, bool>> filterExpression, IClientSessionHandle clientSessionHandle);

    /// <summary>
    /// Finds a document by its unique identifier.
    /// </summary>
    /// <param name="id">The document's identifier.</param>
    /// <param name="options">Optional find options.</param>
    /// <returns>The document if found; otherwise, null.</returns>
    TDocument? FindById(TKey id, FindOptions? options = null);

    /// <summary>
    /// Finds a document by its unique identifier within a transaction session.
    /// </summary>
    /// <param name="id">The document's identifier.</param>
    /// <param name="clientSessionHandle">The MongoDB client session handle.</param>
    /// <param name="options">Optional find options.</param>
    /// <returns>The document if found; otherwise, null.</returns>
    TDocument? FindById(TKey id, IClientSessionHandle clientSessionHandle, FindOptions? options = null);

    /// <summary>
    /// Asynchronously finds a document by its unique identifier.
    /// </summary>
    /// <param name="id">The document's identifier.</param>
    /// <param name="options">Optional find options.</param>
    /// <returns>A task that returns the document if found; otherwise, null.</returns>
    Task<TDocument?> FindByIdAsync(TKey id, FindOptions? options = null);

    /// <summary>
    /// Asynchronously finds a document by its unique identifier within a transaction session.
    /// </summary>
    /// <param name="id">The document's identifier.</param>
    /// <param name="clientSessionHandle">The MongoDB client session handle.</param>
    /// <param name="options">Optional find options.</param>
    /// <returns>A task that returns the document if found; otherwise, null.</returns>
    Task<TDocument?> FindByIdAsync(TKey id, IClientSessionHandle clientSessionHandle, FindOptions? options = null);

    /// <summary>
    /// Inserts a single document into the collection.
    /// </summary>
    /// <param name="document">The document to insert.</param>
    /// <param name="options">Optional insert options.</param>
    void InsertOne(TDocument document, InsertOneOptions? options = null);

    /// <summary>
    /// Inserts a single document into the collection within a transaction session.
    /// </summary>
    /// <param name="document">The document to insert.</param>
    /// <param name="clientSessionHandle">The MongoDB client session handle.</param>
    /// <param name="options">Optional insert options.</param>
    void InsertOne(TDocument document, IClientSessionHandle clientSessionHandle, InsertOneOptions? options = null);

    /// <summary>
    /// Asynchronously inserts a single document into the collection.
    /// </summary>
    /// <param name="document">The document to insert.</param>
    /// <param name="options">Optional insert options.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task InsertOneAsync(TDocument document, InsertOneOptions? options = null);

    /// <summary>
    /// Asynchronously inserts a single document into the collection within a transaction session.
    /// </summary>
    /// <param name="document">The document to insert.</param>
    /// <param name="clientSessionHandle">The MongoDB client session handle.</param>
    /// <param name="options">Optional insert options.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task InsertOneAsync(TDocument document, IClientSessionHandle clientSessionHandle, InsertOneOptions? options = null);

    /// <summary>
    /// Inserts multiple documents into the collection in a single operation.
    /// </summary>
    /// <param name="documents">The collection of documents to insert.</param>
    /// <param name="options">Optional insert options.</param>
    void InsertMany(ICollection<TDocument> documents, InsertManyOptions? options = null);

    /// <summary>
    /// Inserts multiple documents into the collection in a single operation within a transaction session.
    /// </summary>
    /// <param name="documents">The collection of documents to insert.</param>
    /// <param name="clientSessionHandle">The MongoDB client session handle.</param>
    /// <param name="options">Optional insert options.</param>
    void InsertMany(ICollection<TDocument> documents, IClientSessionHandle clientSessionHandle, InsertManyOptions? options = null);

    /// <summary>
    /// Asynchronously inserts multiple documents into the collection.
    /// </summary>
    /// <param name="documents">The collection of documents to insert.</param>
    /// <param name="options">Optional insert options.</param>
    /// <returns>A task representing the asynchronous insert operation.</returns>
    Task InsertManyAsync(ICollection<TDocument> documents, InsertManyOptions? options = null);

    /// <summary>
    /// Asynchronously inserts multiple documents into the collection within a transaction session.
    /// </summary>
    /// <param name="documents">The collection of documents to insert.</param>
    /// <param name="clientSessionHandle">The MongoDB client session handle.</param>
    /// <param name="options">Optional insert options.</param>
    /// <returns>A task representing the asynchronous insert operation.</returns>
    Task InsertManyAsync(ICollection<TDocument> documents, IClientSessionHandle clientSessionHandle, InsertManyOptions? options = null);

    /// <summary>
    /// Asynchronously counts the number of documents matching the filter expression.
    /// </summary>
    /// <param name="filterExpression">Expression to filter documents.</param>
    /// <returns>A task that returns the number of matching documents.</returns>
    Task<long> AsyncCount(Expression<Func<TDocument, bool>> filterExpression);

    /// <summary>
    /// Asynchronously counts the number of documents matching the filter expression within a transaction session.
    /// </summary>
    /// <param name="filterExpression">Expression to filter documents.</param>
    /// <param name="clientSessionHandle">The MongoDB client session handle.</param>
    /// <returns>A task that returns the number of matching documents.</returns>
    Task<long> AsyncCount(Expression<Func<TDocument, bool>> filterExpression, IClientSessionHandle clientSessionHandle);

    /// <summary>
    /// Counts the number of documents matching the filter expression.
    /// </summary>
    /// <param name="filterExpression">Expression to filter documents.</param>
    /// <returns>The number of matching documents.</returns>
    long Count(Expression<Func<TDocument, bool>> filterExpression);

    /// <summary>
    /// Counts the number of documents matching the filter expression within a transaction session.
    /// </summary>
    /// <param name="filterExpression">Expression to filter documents.</param>
    /// <param name="clientSessionHandle">The MongoDB client session handle.</param>
    /// <returns>The number of matching documents.</returns>
    long Count(Expression<Func<TDocument, bool>> filterExpression, IClientSessionHandle clientSessionHandle);

    /// <summary>
    /// Replaces a single document identified by its Id.
    /// </summary>
    /// <param name="document">The new document for replacement.</param>
    /// <param name="options">Options for the replace operation.</param>
    /// <returns>The replaced document.</returns>
    TDocument FindOneAndReplace(TDocument document, FindOneAndReplaceOptions<TDocument>? options = null);

    /// <summary>
    /// Replaces a single document identified by its Id within a transaction session.
    /// </summary>
    /// <param name="document">The new document for replacement.</param>
    /// <param name="clientSessionHandle">The MongoDB client session handle.</param>
    /// <param name="options">Options for the replace operation.</param>
    /// <returns>The replaced document.</returns>
    TDocument FindOneAndReplace(TDocument document, IClientSessionHandle clientSessionHandle, FindOneAndReplaceOptions<TDocument>? options = null);

    /// <summary>
    /// Asynchronously replaces a document identified by its Id.
    /// </summary>
    /// <param name="document">The new document for replacement.</param>
    /// <param name="options">Options for the replace operation.</param>
    /// <returns>A task that returns the replaced document.</returns>
    Task<TDocument> FindOneAndReplaceAsync(TDocument document, FindOneAndReplaceOptions<TDocument>? options = null);

    /// <summary>
    /// Asynchronously replaces a document identified by its Id within a transaction session.
    /// </summary>
    /// <param name="document">The new document for replacement.</param>
    /// <param name="clientSessionHandle">The MongoDB client session handle.</param>
    /// <param name="options">Options for the replace operation.</param>
    /// <returns>A task that returns the replaced document.</returns>
    Task<TDocument> FindOneAndReplaceAsync(TDocument document, IClientSessionHandle clientSessionHandle, FindOneAndReplaceOptions<TDocument>? options = null);

    /// <summary>
    /// Deletes a single document matching the filter expression.
    /// </summary>
    /// <param name="filterExpression">Expression to locate the document to delete.</param>
    /// <param name="options">Options for the delete operation.</param>
    void DeleteOne(Expression<Func<TDocument, bool>> filterExpression, FindOneAndDeleteOptions<TDocument>? options = null);

    /// <summary>
    /// Deletes a single document matching the filter expression within a transaction session.
    /// </summary>
    /// <param name="filterExpression">Expression to locate the document to delete.</param>
    /// <param name="clientSessionHandle">The MongoDB client session handle.</param>
    /// <param name="options">Options for the delete operation.</param>
    void DeleteOne(Expression<Func<TDocument, bool>> filterExpression, IClientSessionHandle clientSessionHandle, FindOneAndDeleteOptions<TDocument>? options = null);

    /// <summary>
    /// Asynchronously deletes a single document matching the filter expression.
    /// </summary>
    /// <param name="filterExpression">Expression to locate the document to delete.</param>
    /// <param name="options">Options for the delete operation.</param>
    /// <returns>A task representing the delete operation.</returns>
    Task DeleteOneAsync(Expression<Func<TDocument, bool>> filterExpression, FindOneAndDeleteOptions<TDocument>? options = null);

    /// <summary>
    /// Asynchronously deletes a single document matching the filter expression within a transaction session.
    /// </summary>
    /// <param name="filterExpression">Expression to locate the document to delete.</param>
    /// <param name="clientSessionHandle">The MongoDB client session handle.</param>
    /// <param name="options">Options for the delete operation.</param>
    /// <returns>A task representing the delete operation.</returns>
    Task DeleteOneAsync(Expression<Func<TDocument, bool>> filterExpression, IClientSessionHandle clientSessionHandle, FindOneAndDeleteOptions<TDocument>? options = null);

    /// <summary>
    /// Deletes a document by its unique identifier.
    /// </summary>
    /// <param name="id">The document's identifier.</param>
    /// <param name="options">Options for the delete operation.</param>
    void DeleteById(TKey id, FindOneAndDeleteOptions<TDocument>? options = null);

    /// <summary>
    /// Deletes a document by its unique identifier within a transaction session.
    /// </summary>
    /// <param name="id">The document's identifier.</param>
    /// <param name="clientSessionHandle">The MongoDB client session handle.</param>
    /// <param name="options">Options for the delete operation.</param>
    void DeleteById(TKey id, IClientSessionHandle clientSessionHandle, FindOneAndDeleteOptions<TDocument>? options = null);

    /// <summary>
    /// Asynchronously deletes a document by its unique identifier.
    /// </summary>
    /// <param name="id">The document's identifier.</param>
    /// <param name="options">Options for the delete operation.</param>
    /// <returns>A task representing the delete operation.</returns>
    Task DeleteByIdAsync(TKey id, FindOneAndDeleteOptions<TDocument>? options = null);

    /// <summary>
    /// Asynchronously deletes a document by its unique identifier within a transaction session.
    /// </summary>
    /// <param name="id">The document's identifier.</param>
    /// <param name="clientSessionHandle">The MongoDB client session handle.</param>
    /// <param name="options">Options for the delete operation.</param>
    /// <returns>A task representing the delete operation.</returns>
    Task DeleteByIdAsync(TKey id, IClientSessionHandle clientSessionHandle, FindOneAndDeleteOptions<TDocument>? options = null);

    /// <summary>
    /// Deletes multiple documents matching the filter expression.
    /// </summary>
    /// <param name="filterExpression">Expression to locate the documents to delete.</param>
    void DeleteMany(Expression<Func<TDocument, bool>> filterExpression);

    /// <summary>
    /// Deletes multiple documents matching the filter expression within a transaction session.
    /// </summary>
    /// <param name="filterExpression">Expression to locate the documents to delete.</param>
    /// <param name="clientSessionHandle">The MongoDB client session handle.</param>
    void DeleteMany(Expression<Func<TDocument, bool>> filterExpression, IClientSessionHandle clientSessionHandle);

    /// <summary>
    /// Asynchronously deletes multiple documents matching the filter expression.
    /// </summary>
    /// <param name="filterExpression">Expression to locate the documents to delete.</param>
    /// <returns>A task representing the delete operation.</returns>
    Task DeleteManyAsync(Expression<Func<TDocument, bool>> filterExpression);

    /// <summary>
    /// Asynchronously deletes multiple documents matching the filter expression within a transaction session.
    /// </summary>
    /// <param name="filterExpression">Expression to locate the documents to delete.</param>
    /// <param name="clientSessionHandle">The MongoDB client session handle.</param>
    /// <returns>A task representing the delete operation.</returns>
    Task DeleteManyAsync(Expression<Func<TDocument, bool>> filterExpression, IClientSessionHandle clientSessionHandle);

    /// <summary>
    /// Executes a series of operations within a MongoDB transaction context asynchronously.
    /// </summary>
    /// <typeparam name="TResult">The type of result returned by the transaction body.</typeparam>
    /// <param name="transactionBody">Function that defines the transactional operations using the provided session.</param>
    /// <returns>A task representing the transactional operation, containing the result of the executed operations.</returns>
    Task<TResult> WithTransactionAsync<TResult>(
        Func<IClientSessionHandle, Task<TResult>> transactionBody);
}