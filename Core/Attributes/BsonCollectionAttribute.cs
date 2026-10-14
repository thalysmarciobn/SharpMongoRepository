namespace SharpMongoRepository;

/// <summary>
///     Declares which MongoDB collection a document type is stored in.
/// </summary>
/// <remarks>
///     The repository resolves the name once per closed generic type and caches it in a static field,
///     so the reflection cost is paid a single time per document type for the lifetime of the process
///     rather than on every repository construction.
/// </remarks>
/// <example>
///     <code>
///     [BsonCollection("orders")]
///     public sealed class Order : IDocument&lt;Guid&gt; { public Guid Id { get; set; } }
///     </code>
/// </example>
/// <param name="collectionName">Name of the collection. Must not be empty or whitespace.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class BsonCollectionAttribute(string collectionName) : Attribute
{
    /// <summary>
    ///     Gets the collection name documents of the decorated type are stored in.
    /// </summary>
    public string CollectionName { get; } = collectionName;
}
