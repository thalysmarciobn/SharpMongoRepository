using System.Linq.Expressions;
using MongoDB.Driver;

namespace MongoRepository.Interface;

/// <summary>
/// Represents a generic repository interface for MongoDB operations.
/// </summary>
/// <typeparam name="TDocument">The document type that implements <see cref="IDocument"/>.</typeparam>
/// <remarks>
/// This interface provides synchronous and asynchronous CRUD operations for MongoDB,
/// with support for queries, projections, and bulk operations.
/// </remarks>
public interface IMongoRepository<TDocument> where TDocument : IDocument
{
    /// <summary>
    /// Finds documents matching the specified filter.
    /// </summary>
    /// <param name="filter">The MongoDB filter definition.</param>
    /// <returns>An <see cref="IFindFluent{TDocument, TDocument}"/> for chaining additional operations.</returns>
    IFindFluent<TDocument, TDocument> Find(FilterDefinition<TDocument> filter);

    /// <summary>
    /// Provides LINQ query support for the document collection.
    /// </summary>
    /// <returns>An <see cref="IQueryable{T}"/> for the document type.</returns>
    IQueryable<TDocument?> AsQueryable();

    /// <summary>
    /// Filters documents by a predicate expression.
    /// </summary>
    /// <param name="filterExpression">The LINQ expression to filter documents.</param>
    /// <returns>An enumerable of matching documents.</returns>
    IEnumerable<TDocument?> FilterBy(Expression<Func<TDocument, bool>> filterExpression);

    /// <summary>
    /// Filters and projects documents using specified expressions.
    /// </summary>
    /// <typeparam name="TProjected">The projection result type.</typeparam>
    /// <param name="filterExpression">The LINQ expression to filter documents.</param>
    /// <param name="projectionExpression">The LINQ expression to project documents.</param>
    /// <returns>An enumerable of projected results.</returns>
    IEnumerable<TProjected> FilterBy<TProjected>(
        Expression<Func<TDocument, bool>> filterExpression,
        Expression<Func<TDocument, TProjected>> projectionExpression);

    /// <summary>
    /// Finds a single document matching the filter expression.
    /// </summary>
    /// <param name="filterExpression">The LINQ expression to filter documents.</param>
    /// <returns>The matching document or null if not found.</returns>
    TDocument? FindOne(Expression<Func<TDocument, bool>> filterExpression);

    /// <summary>
    /// Asynchronously finds a single document matching the filter expression.
    /// </summary>
    /// <param name="filterExpression">The LINQ expression to filter documents.</param>
    /// <returns>A task representing the operation, containing the matching document or null if not found.</returns>
    Task<TDocument?> FindOneAsync(Expression<Func<TDocument, bool>> filterExpression);

    /// <summary>
    /// Finds a document by its unique identifier.
    /// </summary>
    /// <param name="id">The string representation of the document's ObjectId.</param>
    /// <returns>The matching document or null if not found.</returns>
    TDocument? FindById(string id);

    /// <summary>
    /// Asynchronously finds a document by its unique identifier.
    /// </summary>
    /// <param name="id">The string representation of the document's ObjectId.</param>
    /// <returns>A task representing the operation, containing the matching document or null if not found.</returns>
    Task<TDocument?> FindByIdAsync(string id);

    /// <summary>
    /// Inserts a single document into the collection.
    /// </summary>
    /// <param name="document">The document to insert.</param>
    void InsertOne(TDocument document);

    /// <summary>
    /// Asynchronously inserts a single document into the collection.
    /// </summary>
    /// <param name="document">The document to insert.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task InsertOneAsync(TDocument document);

    /// <summary>
    /// Inserts multiple documents into the collection in a single operation.
    /// </summary>
    /// <param name="documents">The collection of documents to insert.</param>
    void InsertMany(ICollection<TDocument> documents);

    /// <summary>
    /// Asynchronously inserts multiple documents into the collection in a single operation.
    /// </summary>
    /// <param name="documents">The collection of documents to insert.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task InsertManyAsync(ICollection<TDocument> documents);

    /// <summary>
    /// Asynchronously counts documents matching the filter expression.
    /// </summary>
    /// <param name="filterExpression">The LINQ expression to filter documents.</param>
    /// <returns>A task representing the operation, containing the count of matching documents.</returns>
    Task<long> AsyncCount(Expression<Func<TDocument, bool>> filterExpression);

    /// <summary>
    /// Counts documents matching the filter expression.
    /// </summary>
    /// <param name="filterExpression">The LINQ expression to filter documents.</param>
    /// <returns>The count of matching documents.</returns>
    long Count(Expression<Func<TDocument, bool>> filterExpression);

    /// <summary>
    /// Replaces a single document (matched by Id) with the provided document.
    /// </summary>
    /// <param name="document">The replacement document.</param>
    void ReplaceOne(TDocument document);

    /// <summary>
    /// Asynchronously replaces a single document (matched by Id) with the provided document.
    /// </summary>
    /// <param name="document">The replacement document.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task ReplaceOneAsync(TDocument document);

    /// <summary>
    /// Deletes a single document matching the filter expression.
    /// </summary>
    /// <param name="filterExpression">The LINQ expression to filter documents.</param>
    void DeleteOne(Expression<Func<TDocument, bool>> filterExpression);

    /// <summary>
    /// Asynchronously deletes a single document matching the filter expression.
    /// </summary>
    /// <param name="filterExpression">The LINQ expression to filter documents.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task DeleteOneAsync(Expression<Func<TDocument, bool>> filterExpression);

    /// <summary>
    /// Deletes a document by its unique identifier.
    /// </summary>
    /// <param name="id">The string representation of the document's ObjectId.</param>
    void DeleteById(string id);

    /// <summary>
    /// Asynchronously deletes a document by its unique identifier.
    /// </summary>
    /// <param name="id">The string representation of the document's ObjectId.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task DeleteByIdAsync(string id);

    /// <summary>
    /// Deletes multiple documents matching the filter expression.
    /// </summary>
    /// <param name="filterExpression">The LINQ expression to filter documents.</param>
    void DeleteMany(Expression<Func<TDocument, bool>> filterExpression);

    /// <summary>
    /// Asynchronously deletes multiple documents matching the filter expression.
    /// </summary>
    /// <param name="filterExpression">The LINQ expression to filter documents.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task DeleteManyAsync(Expression<Func<TDocument, bool>> filterExpression);
}