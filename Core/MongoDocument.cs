using MongoDB.Driver;
using SharpMongoRepository.Compound;
using SharpMongoRepository.Enum;
using SharpMongoRepository.Interface;
using System.Linq.Expressions;

namespace SharpMongoRepository;

/// <summary>
/// Provides helper methods for defining MongoDB indexes for a document type.
/// </summary>
/// <typeparam name="TDocument">The type of the MongoDB document. Must implement <see cref="IDocument"/>.</typeparam>
public static class MongoDocument<TDocument, TId> where TDocument : IDocument<TId>
{
    /// <summary>
    /// Creates an ascending index on a single field.
    /// </summary>
    /// <typeparam name="TKey">The field's data type.</typeparam>
    /// <param name="keySelector">An expression selecting the field.</param>
    /// <param name="unique">Whether the index should enforce uniqueness.</param>
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
    /// Creates a descending index on a single field.
    /// </summary>
    /// <typeparam name="TKey">The field's data type.</typeparam>
    /// <param name="keySelector">An expression selecting the field.</param>
    /// <param name="unique">Whether the index should enforce uniqueness.</param>
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
    /// Creates a compound index on multiple fields, each with its own sort direction.
    /// </summary>
    /// <param name="unique">Whether the index should enforce uniqueness.</param>
    /// <param name="fields">The fields and directions to include in the compound index.</param>
    public static MongoIndex<TDocument, TId> CreateCompoundIndex(
        bool unique = false,
        params CompoundIndexField<TDocument>[] fields)
    {
        if (fields == null || fields.Length == 0)
            throw new ArgumentException("At least one field must be provided.", nameof(fields));

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
    /// Builds a Mongo index object.
    /// </summary>
    private static MongoIndex<TDocument, TId> BuildIndex(IndexKeysDefinition<TDocument> keys, CreateIndexOptions options) =>
        new()
        {
            Keys = keys,
            Options = options
        };

    /// <summary>
    /// Creates options for a MongoDB index with the given name and uniqueness.
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
    /// <param name="keySelector">The expression selecting the field.</param>
    /// <returns>The name of the selected field.</returns>
    /// <exception cref="ArgumentException">Thrown if the expression does not point to a member.</exception>
    private static string GetFieldName(LambdaExpression keySelector)
    {
        if (keySelector == null)
            throw new ArgumentNullException(nameof(keySelector));

        return keySelector.Body switch
        {
            MemberExpression member => member.Member.Name,

            UnaryExpression unary when unary.Operand is MemberExpression operand =>
                operand.Member.Name,

            _ => throw new ArgumentException(
                    $"Expression '{keySelector}' does not refer to a valid member.",
                    nameof(keySelector))
        };
    }

    /// <summary>
    /// Creates a <see cref="CompoundIndexField{TDocument}"/> with the specified direction and field selector.
    /// </summary>
    public static CompoundIndexField<TDocument> Field(IndexDirection direction, Expression<Func<TDocument, object>> keySelector) =>
        new(direction, keySelector);
}