using MongoDB.Bson.Serialization.Attributes;

namespace API.Models;

/// <summary>
///     An audit record with no declared key.
/// </summary>
/// <remarks>
///     <para>
///         Implements no interface and carries no <c>[BsonCollection]</c>: the collection name is given at
///         registration. This is what the keyless repository is for, and what a type from a package you
///         cannot change looks like.
///     </para>
///     <para>
///         <see cref="BsonIgnoreExtraElementsAttribute" /> is required because there is no identifier
///         member: the server stores an <c>_id</c> on every document, and the default mapping rejects an
///         element it cannot place. Where the type is not yours to decorate, the repository option
///         <c>IgnoreExtraElements</c> does the same thing.
///     </para>
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class AuditEntry
{
    /// <summary>Gets or sets what happened.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Gets or sets who did it.</summary>
    public string Actor { get; set; } = string.Empty;

    /// <summary>Gets or sets when it happened.</summary>
    public DateTime At { get; set; }
}
