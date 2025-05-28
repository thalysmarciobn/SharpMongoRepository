using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace SharpMongoRepository.Interface;

/// <summary>
///     Base interface for MongoDB documents to be stored in the repository.
///     Defines the minimum structure that all persisted entities must implement.
/// </summary>
/// <remarks>
///     This interface ensures all documents have a consistent ID field format
///     and can be properly serialized/deserialized by the MongoDB driver.
/// </remarks>
public interface IDocument<T>
{
    /// <summary>
    ///     Gets or sets the unique identifier for the MongoDB document.
    /// </summary>
    /// <value>
    ///     The ObjectId that uniquely identifies this document in the collection.
    /// </value>
    /// <remarks>
    ///     This property is decorated with MongoDB driver attributes:
    ///     <list type="bullet">
    ///         <item>
    ///             <description>[BsonId] - Marks this property as the primary key</description>
    ///         </item>
    ///         <item>
    ///             <description>
    ///                 [BsonRepresentation(BsonType.String)] - Specifies the ID should be stored as string in the
    ///                 database
    ///             </description>
    ///         </item>
    ///     </list>
    ///     The ObjectId type provides document uniqueness and includes timestamp information.
    /// </remarks>
    [BsonId]
    public T? Id { get; set; }
}