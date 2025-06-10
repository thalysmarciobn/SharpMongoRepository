using MongoDB.Driver;
using SharpMongoRepository.Compound;
using SharpMongoRepository.Enum;
using SharpMongoRepository.Interface;
using System.Linq.Expressions;

namespace SharpMongoRepository;

/// <summary>
/// Provides static factory methods for creating <see cref="MongoIndex{TDocument, TId}"/> definitions in a fluent, type-safe way.
/// </summary>
/// <typeparam name="TDocument">The document type, which must implement <see cref="IDocument{TId}"/>.</typeparam>
/// <typeparam name="TId">The type of the document's primary key.</typeparam>
public static class MongoDocument<TDocument, TId> where TDocument : IDocument<TId>
{
    /// <summary>
    /// Creates a definition for a single-field ascending index.
    /// </summary>
    /// <typeparam name="TKey">The type of the property being indexed.</typeparam>
    /// <param name="keySelector">An expression to select the field to index.</param>
    /// <param name="unique">A value indicating whether the index should enforce a unique constraint.</param>
    /// <returns>A <see cref="MongoIndex{TDocument, TId}"/> instance representing the configured index.</returns>
    public static MongoIndex<TDocument, TId> CreateAscendingIndex<TKey>(
        Expression<Func<TDocument, TKey>> keySelector,
        bool unique = false)
    {
        var fieldName = GetFieldName(keySelector);
        var keys = Builders<TDocument>.IndexKeys.Ascending(fieldName);
        var options = BuildOptions(fieldName, unique);

        return BuildIndex(keys, options);
    }

    /// <summary>
    /// Creates a definition for a single-field descending index.
    /// </summary>
    /// <typeparam name="TKey">The type of the property being indexed.</typeparam>
    /// <param name="keySelector">An expression to select the field to index.</param>
    /// <param name="unique">A value indicating whether the index should enforce a unique constraint.</param>
    /// <returns>A <see cref="MongoIndex{TDocument, TId}"/> instance representing the configured index.</returns>
    public static MongoIndex<TDocument, TId> CreateDescendingIndex<TKey>(
        Expression<Func<TDocument, TKey>> keySelector,
        bool unique = false)
    {
        var fieldName = GetFieldName(keySelector);
        var keys = Builders<TDocument>.IndexKeys.Descending(fieldName);
        var options = BuildOptions(fieldName, unique);

        return BuildIndex(keys, options);
    }

    /// <summary>
    /// Creates a definition for a compound (multi-field) index.
    /// </summary>
    /// <param name="unique">A value indicating whether the index should enforce a unique constraint across all combined fields.</param>
    /// <param name="fields">An array of fields to include in the index, each with a specified sort direction.</param>
    /// <returns>A <see cref="MongoIndex{TDocument, TId}"/> instance representing the configured compound index.</returns>
    /// <exception cref="ArgumentException">Thrown when the <paramref name="fields"/> array is null or empty.</exception>
    public static MongoIndex<TDocument, TId> CreateCompoundIndex(
        bool unique = false,
        params CompoundIndexField<TDocument>[] fields)
    {
        if (fields == null || fields.Length == 0)
            throw new ArgumentException("At least one field must be provided for a compound index.", nameof(fields));

        var indexBuilder = Builders<TDocument>.IndexKeys;
        var indexParts = new List<IndexKeysDefinition<TDocument>>();
        var fieldNames = new List<string>();

        foreach (var field in fields)
        {
            var fieldName = GetFieldName(field.KeySelector);
            fieldNames.Add(fieldName);

            var part = field.Direction switch
            {
                IndexDirection.Ascending => indexBuilder.Ascending(fieldName),
                IndexDirection.Descending => indexBuilder.Descending(fieldName),
                _ => throw new ArgumentOutOfRangeException(nameof(field.Direction), "Unsupported index direction.")
            };

            indexParts.Add(part);
        }

        var keys = indexBuilder.Combine(indexParts);
        var options = BuildOptions(string.Join("_", fieldNames), unique);

        return BuildIndex(keys, options);
    }

    /// <summary>
    /// A helper method to create a <see cref="CompoundIndexField{TDocument}"/> for use with <see cref="CreateCompoundIndex"/>.
    /// </summary>
    /// <param name="direction">The sort order for this field.</param>
    /// <param name="keySelector">An expression to select the field.</param>
    /// <returns>A new <see cref="CompoundIndexField{TDocument}"/> instance.</returns>
    public static CompoundIndexField<TDocument> Field(IndexDirection direction, Expression<Func<TDocument, object>> keySelector) =>
        new(direction, keySelector);

    /// <summary>
    /// Composes a MongoIndex object from a keys definition and options.
    /// </summary>
    private static MongoIndex<TDocument, TId> BuildIndex(IndexKeysDefinition<TDocument> keys, CreateIndexOptions options) =>
        new()
        {
            Keys = keys,
            Options = options
        };

    /// <summary>
    /// Creates index options with a specified name and unique constraint.
    /// </summary>
    private static CreateIndexOptions BuildOptions(string name, bool unique) =>
        new()
        {
            Name = name,
            Unique = unique
        };

    /// <summary>
    /// Extracts the field name from a lambda expression.
    /// </summary>
    /// <param name="keySelector">The expression selecting the member.</param>
    /// <returns>The name of the selected member.</returns>
    /// <exception cref="ArgumentException">Thrown if the expression does not refer to a member property.</exception>
    private static string GetFieldName(LambdaExpression keySelector)
    {
        if (keySelector == null)
            throw new ArgumentNullException(nameof(keySelector));

        return keySelector.Body switch
        {
            // Standard member access: x => x.Name
            MemberExpression member => member.Member.Name,

            // Member access on a converted operand: x => (object)x.Name
            UnaryExpression unary when unary.Operand is MemberExpression operand =>
                operand.Member.Name,

            _ => throw new ArgumentException(
                     $"Expression '{keySelector}' does not refer to a valid member.",
                     nameof(keySelector))
        };
    }
}