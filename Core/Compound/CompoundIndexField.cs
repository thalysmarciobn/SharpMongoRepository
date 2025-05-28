using SharpMongoRepository.Enum;
using System;
using System.Linq.Expressions;

namespace SharpMongoRepository.Compound;

/// <summary>
///     Represents a field to be used in a compound index configuration for a MongoDB document.
/// </summary>
/// <typeparam name="TDocument">The document type associated with the index.</typeparam>
/// <remarks>
///     This class encapsulates both the field selector and the desired index direction
///     (ascending or descending) for compound index creation.
/// </remarks>
/// <example>
///     Example usage:
///     <code>
///     var fields = new List&lt;CompoundIndexField&lt;SampleEntity&gt;&gt;
///     {
///         new CompoundIndexField&lt;SampleEntity&gt;(IndexDirection.Ascending, x => x.Email),
///         new CompoundIndexField&lt;SampleEntity&gt;(IndexDirection.Descending, x => x.CreatedAt)
///     };
///     </code>
/// </example>
public class CompoundIndexField<TDocument>
{
    /// <summary>
    ///     Gets the expression used to select the field on which to create the index.
    /// </summary>
    public Expression<Func<TDocument, object>> KeySelector { get; }

    /// <summary>
    ///     Gets the direction of the index (ascending or descending).
    /// </summary>
    public IndexDirection Direction { get; }

    /// <summary>
    ///     Initializes a new instance of the <see cref="CompoundIndexField{TDocument}"/> class.
    /// </summary>
    /// <param name="direction">The direction of the index (ascending or descending).</param>
    /// <param name="keySelector">The field selector expression for the index.</param>
    public CompoundIndexField(IndexDirection direction, Expression<Func<TDocument, object>> keySelector)
    {
        Direction = direction;
        KeySelector = keySelector;
    }
}