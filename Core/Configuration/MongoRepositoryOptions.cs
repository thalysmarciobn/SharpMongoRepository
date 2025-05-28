using SharpMongoRepository.Interface;

namespace SharpMongoRepository.Configuration;

/// <summary>
/// Represents configuration options for the Mongo repository related to a specific document type.
/// </summary>
/// <typeparam name="TDocument">
/// The type of the document, which must implement the <see cref="IDocument"/> interface.
/// </typeparam>
public record MongoRepositoryOptions<TDocument, TKey> where TDocument : IDocument<TKey>
{
    /// <summary>
    /// A list of indexes to be created in MongoDB for the specified document type.
    /// </summary>
    /// <remarks>
    /// These indexes help optimize query performance on the collection.
    /// </remarks>
    public required List<MongoIndex<TDocument, TKey>>? Indexes { get; init; }

    /// <summary>
    /// An optional timeout duration for MongoDB operations.
    /// </summary>
    /// <remarks>
    /// If set, this timeout will be applied to operations to prevent long-running queries.
    /// </remarks>
    public TimeSpan? OperationTimeout { get; init; } = TimeSpan.FromSeconds(30);
}
