using MongoDB.Driver;

namespace SharpMongoRepository.Indexing;

/// <summary>
///     One key of a compound index: a field plus the direction it is indexed in.
/// </summary>
/// <typeparam name="TDocument">Document type the index belongs to.</typeparam>
/// <remarks>
///     Build instances with <see cref="MongoIndexes{TDocument}.Field{TField}" />. The field selector is
///     captured as a strongly typed <see cref="IndexKeysDefinition{TDocument}" /> rather than as a string, so
///     nested paths (<c>x =&gt; x.Address.City</c>) and <c>[BsonElement]</c> renames resolve through the
///     driver's own field resolver.
/// </remarks>
public sealed class CompoundIndexField<TDocument>
{
    internal CompoundIndexField(IndexKeysDefinition<TDocument> keys, IndexDirection direction)
    {
        Keys = keys;
        Direction = direction;
    }

    /// <summary>
    ///     Gets the driver key definition for this field, already carrying its direction.
    /// </summary>
    internal IndexKeysDefinition<TDocument> Keys { get; }

    /// <summary>
    ///     Gets the direction this field is indexed in.
    /// </summary>
    public IndexDirection Direction { get; }
}
