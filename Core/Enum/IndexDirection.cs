namespace SharpMongoRepository.Enum;

/// <summary>
///     Specifies the direction of an index in a MongoDB collection.
/// </summary>
/// <remarks>
///     This enum is typically used when defining compound indexes,
///     to determine whether the index on a field should be in ascending or descending order.
/// </remarks>
/// <example>
///     Example usage:
///     <code>
///     var field = new CompoundIndexField&lt;User&gt;(IndexDirection.Ascending, x => x.Email);
///     </code>
/// </example>
public enum IndexDirection
{
    /// <summary>
    ///     Indicates that the index should be created in ascending order.
    /// </summary>
    Ascending,

    /// <summary>
    ///     Indicates that the index should be created in descending order.
    /// </summary>
    Descending
}