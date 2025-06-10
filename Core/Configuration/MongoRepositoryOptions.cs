using SharpMongoRepository.Interface;

namespace SharpMongoRepository.Configuration;

/// <summary>
/// Defines configuration options for a repository related to a specific document type.
/// </summary>
/// <typeparam name="TDocument">The document type, which must implement <see cref="IDocument{TKey}"/>.</typeparam>
/// <typeparam name="TKey">The type used for the document's primary key.</typeparam>
/// <remarks>
/// This record is used to pass all necessary settings, like index definitions and operation timeouts,
/// when initializing a repository instance.
/// </remarks>
public record MongoRepositoryOptions<TDocument, TKey> where TDocument : IDocument<TKey>
{
    /// <summary>
    /// The list of indexes to be created in the document's collection.
    /// </summary>
    /// <remarks>
    /// The repository will ensure these indexes exist in MongoDB to optimize query performance.
    /// This property is <c>required</c> but can be set to <c>null</c> or an empty list if no indexes are needed.
    /// </remarks>
    public required List<MongoIndex<TDocument, TKey>>? Indexes { get; init; }

    /// <summary>
    /// The timeout duration for individual MongoDB operations.
    /// </summary>
    /// <remarks>
    /// If set, this value prevents database operations from running indefinitely.
    /// It defaults to 30 seconds if not specified.
    /// </remarks>
    public TimeSpan? OperationTimeout { get; init; } = TimeSpan.FromSeconds(30);
}