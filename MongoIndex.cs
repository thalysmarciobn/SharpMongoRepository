using MongoDB.Driver;
using MongoRepository.Interface;

namespace MongoRepository;

/// <summary>
/// Represents a MongoDB index definition for a document type.
/// </summary>
/// <typeparam name="TDocument">The document type that implements <see cref="IDocument"/>.</typeparam>
/// <remarks>
/// This class encapsulates both the index keys definition and creation options
/// to provide a complete configuration for creating indexes in MongoDB.
/// The properties are marked as required to ensure complete initialization.
/// </remarks>
public class MongoIndex<TDocument> where TDocument : IDocument
{
    /// <summary>
    /// Gets the definition of the index keys.
    /// </summary>
    /// <value>
    /// The <see cref="IndexKeysDefinition{TDocument}"/> that specifies which fields to index
    /// and their sort order (ascending/descending).
    /// </value>
    /// <example>
    /// <code>
    /// Builders<Person>.IndexKeys.Ascending(x => x.LastName).Descending(x => x.Age)
    /// </code>
    /// </example>
    public required IndexKeysDefinition<TDocument> Keys { get; init; }

    /// <summary>
    /// Gets the options for creating the index.
    /// </summary>
    /// <value>
    /// The <see cref="CreateIndexOptions"/> that specify additional index properties
    /// such as unique constraints, TTL, or custom name.
    /// </value>
    /// <example>
    /// <code>
    /// new CreateIndexOptions { Unique = true, Name = "unique_email_index" }
    /// </code>
    /// </example>
    public required CreateIndexOptions Options { get; init; }
}