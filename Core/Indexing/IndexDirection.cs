namespace SharpMongoRepository.Indexing;

/// <summary>
///     Sort order of a single key inside an index.
/// </summary>
/// <remarks>
///     Direction only matters for queries that sort on a prefix of a compound index, or that sort on a
///     single-field index in the opposite direction of a second key. It is part of the generated index
///     name, so an ascending and a descending index on the same field are two distinct indexes.
/// </remarks>
public enum IndexDirection
{
    /// <summary>Ascending order, serialised as <c>1</c>.</summary>
    Ascending = 1,

    /// <summary>Descending order, serialised as <c>-1</c>.</summary>
    Descending = -1
}
