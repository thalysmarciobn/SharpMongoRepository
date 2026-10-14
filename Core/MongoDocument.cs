using System.Linq.Expressions;
using SharpMongoRepository.Indexing;

namespace SharpMongoRepository;

/// <summary>
///     Obsolete. Renamed to <see cref="MongoIndexes{TDocument,TKey}" />.
/// </summary>
/// <typeparam name="TDocument">Document type the indexes belong to.</typeparam>
/// <typeparam name="TId">Type of the document's primary key.</typeparam>
/// <remarks>
///     The old name read like a document base class when it was in fact a factory for index definitions.
///     These members forward to <see cref="MongoIndexes{TDocument,TKey}" /> and will be removed in 0.1.0.
/// </remarks>
[Obsolete("Renamed to MongoIndexes<TDocument, TKey> in SharpMongoRepository.Indexing. " +
          "This forwarder will be removed in 0.1.0.")]
public static class MongoDocument<TDocument, TId>
    where TDocument : IDocument<TId>
    where TId : notnull
{
    /// <inheritdoc cref="MongoIndexes{TDocument,TKey}.Ascending{TField}" />
    [Obsolete("Use MongoIndexes<TDocument, TId>.Ascending.")]
    public static MongoIndex<TDocument, TId> CreateAscendingIndex<TField>(
        Expression<Func<TDocument, TField>> keySelector,
        bool unique = false)
        => MongoIndexes<TDocument, TId>.Ascending(keySelector, unique);

    /// <inheritdoc cref="MongoIndexes{TDocument,TKey}.Descending{TField}" />
    [Obsolete("Use MongoIndexes<TDocument, TId>.Descending.")]
    public static MongoIndex<TDocument, TId> CreateDescendingIndex<TField>(
        Expression<Func<TDocument, TField>> keySelector,
        bool unique = false)
        => MongoIndexes<TDocument, TId>.Descending(keySelector, unique);

    /// <inheritdoc cref="MongoIndexes{TDocument,TKey}.Compound(bool, ReadOnlySpan{CompoundIndexField{TDocument}})" />
    [Obsolete("Use MongoIndexes<TDocument, TId>.Compound.")]
    public static MongoIndex<TDocument, TId> CreateCompoundIndex(
        bool unique = false,
        params CompoundIndexField<TDocument>[] fields)
        => MongoIndexes<TDocument, TId>.Compound(unique, fields.AsSpan());

    /// <inheritdoc cref="MongoIndexes{TDocument,TKey}.Field{TField}" />
    [Obsolete("Use MongoIndexes<TDocument, TId>.Field.")]
    public static CompoundIndexField<TDocument> Field(
        IndexDirection direction,
        Expression<Func<TDocument, object>> keySelector)
        => MongoIndexes<TDocument, TId>.Field(direction, keySelector);
}
