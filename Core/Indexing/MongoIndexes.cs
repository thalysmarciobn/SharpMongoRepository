using System.Linq.Expressions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace SharpMongoRepository.Indexing;

/// <summary>
///     Type-safe factory for <see cref="MongoIndex{TDocument,TKey}" /> definitions.
/// </summary>
/// <typeparam name="TDocument">Document type the indexes belong to.</typeparam>
/// <typeparam name="TKey">Type of the document's primary key.</typeparam>
/// <remarks>
///     <para>
///         Field selectors are handed to the driver's own index key builders, so nested paths and
///         <c>[BsonElement]</c> renames resolve correctly: <c>x =&gt; x.Address.City</c> indexes
///         <c>address.city</c>, not <c>city</c>.
///     </para>
///     <para>
///         Index names follow the server's own convention, derived from the rendered keys:
///         <c>{ email: 1, createdAt: -1 }</c> is named <c>email_1_createdAt_-1</c>. Direction is part of the
///         name, so an ascending and a descending index on one field are two distinct, co-existing indexes.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     MongoIndexes&lt;Order, Guid&gt;.Ascending(x =&gt; x.Customer, unique: true);
///
///     MongoIndexes&lt;Order, Guid&gt;.Compound(
///         MongoIndexes&lt;Order, Guid&gt;.Field(IndexDirection.Ascending, x =&gt; x.Customer),
///         MongoIndexes&lt;Order, Guid&gt;.Field(IndexDirection.Descending, x =&gt; x.CreatedAt));
///
///     MongoIndexes&lt;Order, Guid&gt;.TimeToLive(x =&gt; x.CreatedAt, TimeSpan.FromDays(30));
///     </code>
/// </example>
public static class MongoIndexes<TDocument, TKey>
    where TDocument : IDocument<TKey>
    where TKey : notnull
{
    /// <summary>
    ///     Creates a single-field ascending index.
    /// </summary>
    /// <typeparam name="TField">Type of the indexed field.</typeparam>
    /// <param name="field">Selector for the field to index. Nested paths are supported.</param>
    /// <param name="unique">Whether the index enforces uniqueness.</param>
    /// <param name="configure">Optional hook to set further options such as sparse or a partial filter.</param>
    /// <returns>The index definition.</returns>
    public static MongoIndex<TDocument, TKey> Ascending<TField>(
        Expression<Func<TDocument, TField>> field,
        bool unique = false,
        Action<CreateIndexOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(field);
        return Build(Builders<TDocument>.IndexKeys.Ascending(ToField(field)), unique, configure);
    }

    /// <summary>
    ///     Creates a single-field descending index.
    /// </summary>
    /// <typeparam name="TField">Type of the indexed field.</typeparam>
    /// <param name="field">Selector for the field to index. Nested paths are supported.</param>
    /// <param name="unique">Whether the index enforces uniqueness.</param>
    /// <param name="configure">Optional hook to set further options such as sparse or a partial filter.</param>
    /// <returns>The index definition.</returns>
    public static MongoIndex<TDocument, TKey> Descending<TField>(
        Expression<Func<TDocument, TField>> field,
        bool unique = false,
        Action<CreateIndexOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(field);
        return Build(Builders<TDocument>.IndexKeys.Descending(ToField(field)), unique, configure);
    }

    /// <summary>
    ///     Creates a compound index over two or more fields.
    /// </summary>
    /// <param name="unique">Whether the combination of all keys must be unique.</param>
    /// <param name="fields">
    ///     Fields in index order, built with <see cref="Field{TField}" />. Order matters: a compound index
    ///     only serves queries that filter on a prefix of its keys.
    /// </param>
    /// <returns>The index definition.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="fields" /> is empty.</exception>
    public static MongoIndex<TDocument, TKey> Compound(
        bool unique,
        params ReadOnlySpan<CompoundIndexField<TDocument>> fields)
        => Compound(unique, null, fields);

    /// <summary>
    ///     Creates a non-unique compound index over two or more fields.
    /// </summary>
    /// <param name="fields">Fields in index order, built with <see cref="Field{TField}" />.</param>
    /// <returns>The index definition.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="fields" /> is empty.</exception>
    public static MongoIndex<TDocument, TKey> Compound(
        params ReadOnlySpan<CompoundIndexField<TDocument>> fields)
        => Compound(false, null, fields);

    /// <summary>
    ///     Creates a compound index over two or more fields, with access to the full option set.
    /// </summary>
    /// <param name="unique">Whether the combination of all keys must be unique.</param>
    /// <param name="configure">Optional hook to set further options such as a partial filter expression.</param>
    /// <param name="fields">Fields in index order, built with <see cref="Field{TField}" />.</param>
    /// <returns>The index definition.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="fields" /> is empty.</exception>
    public static MongoIndex<TDocument, TKey> Compound(
        bool unique,
        Action<CreateIndexOptions>? configure,
        params ReadOnlySpan<CompoundIndexField<TDocument>> fields)
    {
        if (fields.IsEmpty)
            throw new ArgumentException("A compound index needs at least one field.", nameof(fields));

        var parts = new IndexKeysDefinition<TDocument>[fields.Length];
        for (var i = 0; i < fields.Length; i++)
        {
            var field = fields[i] ?? throw new ArgumentException(
                $"Field at position {i} is null.", nameof(fields));
            parts[i] = field.Keys;
        }

        return Build(Builders<TDocument>.IndexKeys.Combine(parts), unique, configure);
    }

    /// <summary>
    ///     Creates a TTL index: the server deletes documents once the indexed date is older than
    ///     <paramref name="expireAfter" />.
    /// </summary>
    /// <typeparam name="TField">Type of the indexed field. Must serialise to a BSON date.</typeparam>
    /// <param name="field">Selector for the date field driving expiry.</param>
    /// <param name="expireAfter">How long after that date a document becomes eligible for deletion.</param>
    /// <param name="configure">Optional hook to set further options.</param>
    /// <returns>The index definition.</returns>
    /// <remarks>
    ///     Expiry is swept by a background task roughly every 60 seconds, so deletion is eventual rather
    ///     than immediate. A TTL index cannot be unique.
    /// </remarks>
    public static MongoIndex<TDocument, TKey> TimeToLive<TField>(
        Expression<Func<TDocument, TField>> field,
        TimeSpan expireAfter,
        Action<CreateIndexOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(field);
        return Build(Builders<TDocument>.IndexKeys.Ascending(ToField(field)), unique: false, options =>
        {
            options.ExpireAfter = expireAfter;
            configure?.Invoke(options);
        });
    }

    /// <summary>
    ///     Creates a text index, enabling <c>$text</c> search over the field.
    /// </summary>
    /// <typeparam name="TField">Type of the indexed field.</typeparam>
    /// <param name="field">Selector for the field to index.</param>
    /// <param name="configure">Optional hook to set weights, default language, and so on.</param>
    /// <returns>The index definition.</returns>
    /// <remarks>A collection may hold at most one text index, though it can cover several fields.</remarks>
    public static MongoIndex<TDocument, TKey> Text<TField>(
        Expression<Func<TDocument, TField>> field,
        Action<CreateIndexOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(field);
        return Build(Builders<TDocument>.IndexKeys.Text(ToField(field)), unique: false, configure);
    }

    /// <summary>
    ///     Creates a hashed index, used as a shard key for even distribution.
    /// </summary>
    /// <typeparam name="TField">Type of the indexed field.</typeparam>
    /// <param name="field">Selector for the field to index.</param>
    /// <param name="configure">Optional hook to set further options.</param>
    /// <returns>The index definition.</returns>
    /// <remarks>A hashed index cannot serve range queries or sorts.</remarks>
    public static MongoIndex<TDocument, TKey> Hashed<TField>(
        Expression<Func<TDocument, TField>> field,
        Action<CreateIndexOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(field);
        return Build(Builders<TDocument>.IndexKeys.Hashed(ToField(field)), unique: false, configure);
    }

    /// <summary>
    ///     Wraps a hand-built key definition, for index shapes this factory does not cover.
    /// </summary>
    /// <param name="keys">The key definition.</param>
    /// <param name="unique">Whether the index enforces uniqueness.</param>
    /// <param name="configure">Optional hook to set further options.</param>
    /// <returns>The index definition.</returns>
    /// <remarks>
    ///     The name is still derived from the rendered keys unless <paramref name="configure" /> overrides it.
    /// </remarks>
    public static MongoIndex<TDocument, TKey> Custom(
        IndexKeysDefinition<TDocument> keys,
        bool unique = false,
        Action<CreateIndexOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(keys);
        return Build(keys, unique, configure);
    }

    /// <summary>
    ///     Describes one key of a compound index.
    /// </summary>
    /// <typeparam name="TField">Type of the field. Inferred from <paramref name="field" />.</typeparam>
    /// <param name="direction">Direction to index the field in.</param>
    /// <param name="field">Selector for the field. Nested paths are supported.</param>
    /// <returns>A key description to pass to <see cref="Compound(bool, ReadOnlySpan{CompoundIndexField{TDocument}})" />.</returns>
    public static CompoundIndexField<TDocument> Field<TField>(
        IndexDirection direction,
        Expression<Func<TDocument, TField>> field)
    {
        ArgumentNullException.ThrowIfNull(field);

        var keys = direction switch
        {
            IndexDirection.Ascending => Builders<TDocument>.IndexKeys.Ascending(ToField(field)),
            IndexDirection.Descending => Builders<TDocument>.IndexKeys.Descending(ToField(field)),
            _ => throw new ArgumentOutOfRangeException(
                nameof(direction), direction, "Unsupported index direction.")
        };

        return new CompoundIndexField<TDocument>(keys, direction);
    }

    /// <summary>
    ///     Wraps a typed selector as a field definition.
    /// </summary>
    /// <remarks>
    ///     The driver's index key builders accept either a field definition or an
    ///     <c>Expression&lt;Func&lt;TDocument, object&gt;&gt;</c>. Going through
    ///     <see cref="ExpressionFieldDefinition{TDocument,TField}" /> keeps the selector strongly typed and,
    ///     more importantly, lets the driver resolve the whole member path. Reading the member name off the
    ///     expression by hand, as the previous version did, turned <c>x =&gt; x.Address.City</c> into an index
    ///     on a root-level <c>City</c> field that no document has.
    /// </remarks>
    private static FieldDefinition<TDocument> ToField<TField>(Expression<Func<TDocument, TField>> field)
        => new ExpressionFieldDefinition<TDocument, TField>(field);

    private static MongoIndex<TDocument, TKey> Build(
        IndexKeysDefinition<TDocument> keys,
        bool unique,
        Action<CreateIndexOptions>? configure)
    {
        var options = new CreateIndexOptions { Unique = unique };
        var index = new MongoIndex<TDocument, TKey> { Keys = keys, Options = options };

        // Derive the name from the rendered keys so it reflects the real BSON field names and
        // directions. Done before `configure` runs, so a caller can still override it.
        options.Name = BuildIndexName(index.RenderKeys());
        configure?.Invoke(options);

        if (string.IsNullOrWhiteSpace(options.Name))
            throw new RepositoryException("An index name must not be empty.");

        return index;
    }

    /// <summary>
    ///     Reproduces the server's default index naming: each key as <c>name_value</c>, joined by <c>_</c>.
    /// </summary>
    internal static string BuildIndexName(BsonDocument renderedKeys)
    {
        if (renderedKeys.ElementCount == 0)
            throw new RepositoryException("An index must contain at least one key.");

        var parts = new string[renderedKeys.ElementCount];
        var i = 0;

        foreach (var element in renderedKeys)
        {
            // Numeric directions render as 1 / -1; text and hashed indexes render as strings.
            var value = element.Value.BsonType switch
            {
                BsonType.String => element.Value.AsString,
                BsonType.Double => element.Value.AsDouble.ToString("0.####",
                    System.Globalization.CultureInfo.InvariantCulture),
                _ => element.Value.ToString()
            };

            parts[i++] = $"{element.Name}_{value}";
        }

        return string.Join('_', parts);
    }
}
