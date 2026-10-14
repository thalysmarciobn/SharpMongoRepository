using System.Linq.Expressions;
using SharpMongoRepository.Indexing;

namespace SharpMongoRepository;

/// <summary>
///     Obsolete. Renamed to <see cref="MongoIndexes{TDocument}" />.
/// </summary>
/// <typeparam name="TDocument">Document type the indexes belong to.</typeparam>
/// <typeparam name="TId">Type of the document's primary key.</typeparam>
/// <remarks>
///     The old name read like a document base class when it was in fact a factory for index definitions.
///     These members forward to <see cref="MongoIndexes{TDocument}" /> and will be removed in 0.2.0.
/// </remarks>
[Obsolete("Renamed to MongoIndexes<TDocument, TKey> in SharpMongoRepository.Indexing. " +
          "This forwarder will be removed in 0.2.0.")]
public static class MongoDocument<TDocument, TId>
    where TDocument : IDocument<TId>
    where TId : notnull
{
    /// <inheritdoc cref="MongoIndexes{TDocument}.Ascending{TField}" />
    [Obsolete("Use MongoIndexes<TDocument>.Ascending.")]
    public static MongoIndex<TDocument> CreateAscendingIndex<TField>(
        Expression<Func<TDocument, TField>> keySelector,
        bool unique = false)
        => MongoIndexes<TDocument>.Ascending(keySelector, unique);

    /// <inheritdoc cref="MongoIndexes{TDocument}.Descending{TField}" />
    [Obsolete("Use MongoIndexes<TDocument>.Descending.")]
    public static MongoIndex<TDocument> CreateDescendingIndex<TField>(
        Expression<Func<TDocument, TField>> keySelector,
        bool unique = false)
        => MongoIndexes<TDocument>.Descending(keySelector, unique);

    /// <inheritdoc cref="MongoIndexes{TDocument}.Compound(bool, ReadOnlySpan{CompoundIndexField{TDocument}})" />
    [Obsolete("Use MongoIndexes<TDocument>.Compound.")]
    public static MongoIndex<TDocument> CreateCompoundIndex(
        bool unique = false,
        params CompoundIndexField<TDocument>[] fields)
        => MongoIndexes<TDocument>.Compound(unique, fields.AsSpan());

    /// <inheritdoc cref="MongoIndexes{TDocument}.Field{TField}" />
    [Obsolete("Use MongoIndexes<TDocument>.Field.")]
    public static CompoundIndexField<TDocument> Field(
        IndexDirection direction,
        Expression<Func<TDocument, object>> keySelector)
        => MongoIndexes<TDocument>.Field(direction, keySelector);
}
