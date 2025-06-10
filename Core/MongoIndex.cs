using MongoDB.Driver;
using SharpMongoRepository.Interface;

namespace SharpMongoRepository;

/// <summary>
/// Represents a complete MongoDB index definition for a specific document type.
/// </summary>
/// <typeparam name="TDocument">The document type that implements <see cref="IDocument{TKey}"/>.</typeparam>
/// <typeparam name="TKey">The type used for the document's primary key.</typeparam>
/// <remarks>
/// This class encapsulates both the index keys and creation options, providing a full
/// configuration for creating an index in MongoDB.
/// The properties are marked as <c>required</c> to ensure that a complete definition
/// is provided upon initialization.
/// </remarks>
public class MongoIndex<TDocument, TKey> where TDocument : IDocument<TKey>
{
    /// <summary>
    /// The definition of the index keys.
    /// </summary>
    /// <value>
    /// An <see cref="IndexKeysDefinition{TDocument}"/> that specifies which fields to index
    /// and their sort order (ascending/descending).
    /// </value>
    /// <example>
    /// Defining a compound index on LastName (ascending) and Age (descending):
    /// <code>
    /// Builders&lt;Person&gt;.IndexKeys.Ascending(x => x.LastName).Descending(x => x.Age)
    /// </code>
    /// </example>
    public required IndexKeysDefinition<TDocument> Keys { get; init; }

    /// <summary>
    /// The options for creating the index.
    /// </summary>
    /// <value>
    /// A <see cref="CreateIndexOptions"/> object that specifies additional index properties
    /// such as unique constraints, a custom name, or a TTL (Time-To-Live).
    /// </value>
    /// <example>
    /// Setting options for a unique index with a custom name:
    /// <code>
    /// new CreateIndexOptions { Unique = true, Name = "unique_email_index" }
    /// </code>
    /// </example>
    public required CreateIndexOptions Options { get; init; }
}