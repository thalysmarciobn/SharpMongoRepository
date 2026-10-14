using MongoDB.Bson.Serialization.Attributes;

namespace SharpMongoRepository;

/// <summary>
///     Contract every document stored through <see cref="IMongoRepository{TDocument,TKey}" /> must satisfy.
/// </summary>
/// <typeparam name="TKey">
///     Type of the document's primary key. <c>ObjectId</c>, <see cref="System.Guid" /> and
///     <see cref="string" /> get an identifier generator out of the box; any other type requires
///     <see cref="MongoRepositoryOptions{TDocument,TKey}.IdGenerator" /> or an identifier assigned by the caller.
/// </typeparam>
/// <remarks>
///     <para>
///         The MongoDB driver maps the primary key by convention: a member named <c>Id</c> becomes <c>_id</c>.
///         The <see cref="BsonIdAttribute" /> below documents that intent but does not drive the mapping,
///         because BSON class mapping inspects the implementing class rather than its interfaces. If the
///         implementing type names its key something other than <c>Id</c>, it must apply
///         <see cref="BsonIdAttribute" /> itself.
///     </para>
///     <para>
///         <typeparamref name="TKey" /> is constrained to <c>notnull</c> so consumers do not have to
///         null-forgive every access to <see cref="Id" />.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     [BsonCollection("orders")]
///     public sealed class Order : IDocument&lt;Guid&gt;
///     {
///         public Guid Id { get; set; }
///         public string Customer { get; set; } = string.Empty;
///     }
///     </code>
/// </example>
public interface IDocument<TKey>
    where TKey : notnull
{
    /// <summary>
    ///     Gets or sets the document's primary key, stored as <c>_id</c>.
    /// </summary>
    /// <remarks>
    ///     Leave it at its default value on insert to have the repository generate one. The setter is
    ///     required precisely so that generated identifiers can be written back onto the instance you passed in.
    /// </remarks>
    [BsonId]
    TKey Id { get; set; }
}
