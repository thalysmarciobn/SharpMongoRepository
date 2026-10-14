using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace SharpMongoRepository.Indexing;

/// <summary>
///     A complete index definition: the keys, and the options to create them with.
/// </summary>
/// <typeparam name="TDocument">Document type the index belongs to.</typeparam>
/// <remarks>
///     Build instances through <see cref="MongoIndexes{TDocument}" /> rather than by hand. The factory
///     derives <see cref="CreateIndexOptions.Name" /> from the rendered BSON keys, which keeps the name in
///     step with the actual indexed fields and their directions.
/// </remarks>
public sealed class MongoIndex<TDocument>
{
    /// <summary>
    ///     Gets the index key definition.
    /// </summary>
    public required IndexKeysDefinition<TDocument> Keys { get; init; }

    /// <summary>
    ///     Gets the index creation options. <see cref="CreateIndexOptions.Name" /> is required.
    /// </summary>
    public required CreateIndexOptions Options { get; init; }

    /// <summary>
    ///     Renders the key definition to the BSON document the server will receive.
    /// </summary>
    /// <returns>The rendered keys, for example <c>{ "email" : 1, "createdAt" : -1 }</c>.</returns>
    /// <remarks>
    ///     Used to compare a configured index against what already exists on the server, so a changed
    ///     definition can be detected instead of silently ignored.
    /// </remarks>
    public BsonDocument RenderKeys()
    {
        var registry = BsonSerializer.SerializerRegistry;
        return Keys.Render(new RenderArgs<TDocument>(registry.GetSerializer<TDocument>(), registry));
    }
}
