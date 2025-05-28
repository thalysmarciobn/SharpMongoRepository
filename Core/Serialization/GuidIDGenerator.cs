using MongoDB.Bson.Serialization;

namespace SharpMongoRepository.Serialization;

/// <summary>
///     Generates new <see cref="Guid"/> identifiers for MongoDB documents.
/// </summary>
/// <remarks>
///     Implements the <see cref="IIdGenerator"/> interface from MongoDB driver to provide
///     GUID-based IDs for documents during serialization.
/// </remarks>
internal class GuidIDGenerator : IIdGenerator
{
    /// <summary>
    ///     Generates a new <see cref="Guid"/> to be used as an identifier.
    /// </summary>
    /// <param name="container">The container object that holds the document (not used).</param>
    /// <param name="document">The document for which the ID is generated (not used).</param>
    /// <returns>A new <see cref="Guid"/> value.</returns>
    public object GenerateId(object container, object document)
    {
        return Guid.NewGuid();
    }

    /// <summary>
    ///     Determines whether the provided ID is considered empty or uninitialized.
    /// </summary>
    /// <param name="id">The ID value to check.</param>
    /// <returns>
    ///     <see langword="true"/> if the ID is <see langword="null"/> or equals <see cref="Guid.Empty"/>; otherwise, <see langword="false"/>.
    /// </returns>
    public bool IsEmpty(object id)
    {
        return id == null || (Guid)id == Guid.Empty;
    }
}