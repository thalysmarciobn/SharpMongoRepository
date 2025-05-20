namespace SharpMongoRepository.Attributes;

/// <summary>
///     Specifies the MongoDB collection name for a document type.
/// </summary>
/// <remarks>
///     This attribute is used to decorate document classes to indicate
///     which MongoDB collection they should be stored in.
///     The attribute is not inherited by derived classes.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class BsonCollectionAttribute(string? collectionName) : Attribute
{
    /// <summary>
    ///     Gets the name of the MongoDB collection for the decorated document type.
    /// </summary>
    /// <value>
    ///     The name of the MongoDB collection where documents of this type will be stored.
    /// </value>
    public string? CollectionName { get; } = collectionName;
}