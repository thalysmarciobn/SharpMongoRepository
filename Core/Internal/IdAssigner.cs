using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.IdGenerators;

namespace SharpMongoRepository.Internal;

/// <summary>
///     Fills in empty primary keys before an insert.
/// </summary>
/// <typeparam name="TDocument">Document type being inserted.</typeparam>
/// <typeparam name="TKey">Type of the document's primary key.</typeparam>
/// <remarks>
///     <para>
///         The generator is resolved once per closed generic type into a <c>static readonly</c> field, so the
///         type inspection happens a single time per process rather than per repository or per insert.
///     </para>
///     <para>
///         Emptiness is tested with <see cref="EqualityComparer{T}.Default" />, which the JIT devirtualises
///         and inlines for value-type keys. That keeps the check free of the boxing that
///         <see cref="IIdGenerator.IsEmpty" /> forces, and it is only called for a caller-supplied generator.
///     </para>
/// </remarks>
internal static class IdAssigner<TDocument, TKey>
    where TDocument : IDocument<TKey>
    where TKey : notnull
{
    private static readonly bool KeyIsString = typeof(TKey) == typeof(string);

    private static readonly IIdGenerator? DefaultGenerator = ResolveDefaultGenerator();

    /// <summary>
    ///     Assigns a generated key to <paramref name="document" /> when its key is still empty.
    /// </summary>
    /// <param name="document">The document about to be inserted.</param>
    /// <param name="configured">
    ///     A generator supplied through <see cref="MongoRepositoryOptions{TDocument}.IdGenerator" />,
    ///     or <see langword="null" /> to use the default for <typeparamref name="TKey" />.
    /// </param>
    internal static void Assign(TDocument document, IIdGenerator? configured)
    {
        var generator = configured ?? DefaultGenerator;
        if (generator is null)
            return;

        if (IsEmpty(document.Id, generator, configured is not null))
            document.Id = (TKey)generator.GenerateId(null, document);
    }

    /// <summary>
    ///     Assigns generated keys to every document in <paramref name="documents" /> whose key is empty.
    /// </summary>
    /// <param name="documents">The documents about to be inserted.</param>
    /// <param name="configured">
    ///     A generator supplied through <see cref="MongoRepositoryOptions{TDocument}.IdGenerator" />,
    ///     or <see langword="null" /> to use the default for <typeparamref name="TKey" />.
    /// </param>
    /// <remarks>
    ///     The previous version only generated keys in the single-document insert path. With a
    ///     <see cref="string" /> key, for which the driver registers no generator of its own, that made
    ///     <c>InsertMany</c> write <c>_id: null</c> for every document and fail with a duplicate key error on
    ///     the second one.
    /// </remarks>
    internal static void Assign(IEnumerable<TDocument> documents, IIdGenerator? configured)
    {
        var generator = configured ?? DefaultGenerator;
        if (generator is null)
            return;

        var custom = configured is not null;

        // A list or array is the overwhelmingly common case; indexing it avoids an enumerator allocation.
        if (documents is IList<TDocument> list)
        {
            for (var i = 0; i < list.Count; i++)
            {
                var document = list[i];
                if (IsEmpty(document.Id, generator, custom))
                    document.Id = (TKey)generator.GenerateId(null, document);
            }

            return;
        }

        foreach (var document in documents)
            if (IsEmpty(document.Id, generator, custom))
                document.Id = (TKey)generator.GenerateId(null, document);
    }

    private static bool IsEmpty(TKey id, IIdGenerator generator, bool custom)
    {
        // A caller-supplied generator defines its own notion of empty, so defer to it.
        if (custom)
            return generator.IsEmpty(id);

        // A string key is empty when it is null or "", not merely when it is null, because a non-nullable
        // declaration is usually initialised to string.Empty.
        if (KeyIsString)
            return string.IsNullOrEmpty(id as string);

        return EqualityComparer<TKey>.Default.Equals(id, default!);
    }

    /// <summary>
    ///     Picks the generator for <typeparamref name="TKey" />, or <see langword="null" /> when the caller
    ///     is responsible for assigning identifiers.
    /// </summary>
    /// <remarks>
    ///     Returning <see langword="null" /> rather than throwing is deliberate. The previous version threw
    ///     from the repository constructor for any unrecognised key type, which made a document with, say, an
    ///     <see cref="int" /> key unusable even when the caller always supplied the identifier itself.
    /// </remarks>
    private static IIdGenerator? ResolveDefaultGenerator()
    {
        if (typeof(TKey) == typeof(ObjectId))
            return ObjectIdGenerator.Instance;

        if (typeof(TKey) == typeof(Guid))
            return GuidGenerator.Instance;

        if (typeof(TKey) == typeof(string))
            return StringObjectIdGenerator.Instance;

        return null;
    }
}
