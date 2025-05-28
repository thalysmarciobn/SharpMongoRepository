using SharpMongoRepository.Compound;
using SharpMongoRepository.Enum;
using System.Linq.Expressions;

namespace SharpMongoRepository.Helper;

/// <summary>
///     Provides a fluent-style helper method for defining compound index fields in MongoDB.
/// </summary>
/// <typeparam name="TDocument">The type of the MongoDB document.</typeparam>
/// <remarks>
///     This helper is primarily used to simplify the creation of <see cref="CompoundIndexField{TDocument}"/> instances
///     when configuring compound indexes on collections.
/// </remarks>
/// <example>
///     Example usage when defining compound indexes:
///     <code>
///     var fields = new List&lt;CompoundIndexField&lt;User&gt;&gt;
///     {
///         CompoundHelper&lt;User&gt;.Field(IndexDirection.Ascending, x => x.Email),
///         CompoundHelper&lt;User&gt;.Field(IndexDirection.Descending, x => x.CreatedAt)
///     };
///     </code>
/// </example>
internal class CompoundHelper<TDocument>
{
    /// <summary>
    ///     Creates a compound index field definition using the provided direction and key selector.
    /// </summary>
    /// <param name="direction">The direction of the index (ascending or descending).</param>
    /// <param name="keySelector">An expression specifying the document field to index.</param>
    /// <returns>A <see cref="CompoundIndexField{TDocument}"/> representing the index field definition.</returns>
    public static CompoundIndexField<TDocument> Field(IndexDirection direction, Expression<Func<TDocument, object>> keySelector)
    {
        return new CompoundIndexField<TDocument>(direction, keySelector);
    }
}