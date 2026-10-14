using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using SharpMongoRepository.Indexing;

namespace SharpMongoRepository.Internal;

/// <summary>
///     Creates a collection's configured indexes exactly once per process, and reports definitions that have
///     drifted from what the server already holds.
/// </summary>
/// <remarks>
///     <para>
///         Registered as a singleton so the work is shared by every repository instance. The previous version
///         did this from inside a per-instance <see cref="Lazy{T}" />; with a scoped repository registration
///         that meant a <c>listIndexes</c> round-trip on every HTTP request.
///     </para>
///     <para>
///         A failed attempt is not recorded as complete, so the next call retries.
///         <see cref="LazyThreadSafetyMode.ExecutionAndPublication" />, used previously, caches the exception
///         instead and leaves the repository permanently broken after one transient network fault.
///     </para>
/// </remarks>
internal sealed class MongoIndexEnsurer : IDisposable
{
    // Keyed on the client instance itself rather than a hash of it, so two identically named collections on
    // different clusters cannot be mistaken for one another.
    private readonly ConcurrentDictionary<(IMongoClient Client, string Namespace), byte> _completed = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    public void Dispose() => _gate.Dispose();

    /// <summary>
    ///     Ensures the configured indexes exist, doing nothing on subsequent calls for the same collection.
    /// </summary>
    internal Task EnsureAsync<TDocument, TKey>(
        IMongoCollection<TDocument> collection,
        MongoRepositoryOptions<TDocument, TKey> options,
        ILogger logger,
        CancellationToken cancellationToken)
        where TDocument : IDocument<TKey>
        where TKey : notnull
    {
        // Fast path: a completed Task is cached by the runtime, so a warm call allocates nothing.
        return IsComplete(collection, options)
            ? Task.CompletedTask
            : EnsureSlowAsync(collection, options, logger, cancellationToken);
    }

    /// <summary>
    ///     Synchronous counterpart of <see cref="EnsureAsync{TDocument,TKey}" />.
    /// </summary>
    internal void Ensure<TDocument, TKey>(
        IMongoCollection<TDocument> collection,
        MongoRepositoryOptions<TDocument, TKey> options,
        ILogger logger,
        CancellationToken cancellationToken)
        where TDocument : IDocument<TKey>
        where TKey : notnull
    {
        if (IsComplete(collection, options))
            return;

        var key = KeyFor(collection);

        _gate.Wait(cancellationToken);
        try
        {
            if (_completed.ContainsKey(key))
                return;

            var existing = collection.Indexes.List(cancellationToken).ToList(cancellationToken);
            var models = Plan(collection, options, existing, logger, dropAsync: null, cancellationToken);

            if (models.Count > 0)
                collection.Indexes.CreateMany(models, cancellationToken);

            _completed[key] = 0;
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool IsComplete<TDocument, TKey>(
        IMongoCollection<TDocument> collection,
        MongoRepositoryOptions<TDocument, TKey> options)
        where TDocument : IDocument<TKey>
        where TKey : notnull
        => options.IndexCreation == IndexCreationMode.Disabled
           || options.Indexes.Count == 0
           || _completed.ContainsKey(KeyFor(collection));

    private async Task EnsureSlowAsync<TDocument, TKey>(
        IMongoCollection<TDocument> collection,
        MongoRepositoryOptions<TDocument, TKey> options,
        ILogger logger,
        CancellationToken cancellationToken)
        where TDocument : IDocument<TKey>
        where TKey : notnull
    {
        var key = KeyFor(collection);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_completed.ContainsKey(key))
                return;

            var cursor = await collection.Indexes.ListAsync(cancellationToken).ConfigureAwait(false);
            var existing = await cursor.ToListAsync(cancellationToken).ConfigureAwait(false);

            var drops = new List<string>();
            var models = Plan(collection, options, existing, logger, drops, cancellationToken);

            foreach (var name in drops)
                await collection.Indexes.DropOneAsync(name, cancellationToken).ConfigureAwait(false);

            // One createIndexes command for the whole set rather than one per index.
            if (models.Count > 0)
                await collection.Indexes.CreateManyAsync(models, cancellationToken).ConfigureAwait(false);

            _completed[key] = 0;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    ///     Works out which indexes are missing and which have drifted.
    /// </summary>
    /// <remarks>
    ///     <c>dropAsync</c> collects the names to drop before creating, for the asynchronous caller. When it
    ///     is <see langword="null" />, drift is dropped inline through the synchronous API instead.
    /// </remarks>
    private static List<CreateIndexModel<TDocument>> Plan<TDocument, TKey>(
        IMongoCollection<TDocument> collection,
        MongoRepositoryOptions<TDocument, TKey> options,
        List<BsonDocument> existing,
        ILogger logger,
        List<string>? dropAsync,
        CancellationToken cancellationToken)
        where TDocument : IDocument<TKey>
        where TKey : notnull
    {
        var byName = new Dictionary<string, BsonDocument>(existing.Count, StringComparer.Ordinal);
        foreach (var index in existing)
            if (index.TryGetValue("name", out var name) && name.IsString)
                byName[name.AsString] = index;

        var models = new List<CreateIndexModel<TDocument>>(options.Indexes.Count);

        foreach (var index in options.Indexes)
        {
            var name = index.Options.Name
                       ?? throw new RepositoryException("An index name must be provided.");

            if (!byName.TryGetValue(name, out var current))
            {
                models.Add(new CreateIndexModel<TDocument>(index.Keys, index.Options));
                continue;
            }

            if (Matches(current, index))
                continue;

            if (!options.RecreateChangedIndexes)
            {
                logger.LogWarning(
                    "Index '{IndexName}' on {Collection} differs from its configured definition and was left " +
                    "untouched. Set RecreateChangedIndexes to drop and rebuild it.",
                    name, collection.CollectionNamespace.FullName);
                continue;
            }

            logger.LogInformation(
                "Index '{IndexName}' on {Collection} differs from its configured definition; rebuilding.",
                name, collection.CollectionNamespace.FullName);

            if (dropAsync is not null)
                dropAsync.Add(name);
            else
                collection.Indexes.DropOne(name, cancellationToken);

            models.Add(new CreateIndexModel<TDocument>(index.Keys, index.Options));
        }

        return models;
    }

    /// <summary>
    ///     Compares a server index definition against a configured one.
    /// </summary>
    /// <remarks>
    ///     Key order is significant for compound indexes, and <see cref="BsonDocument" /> equality is
    ///     order-sensitive, so a straight comparison is the correct test. Partial filter expressions are not
    ///     compared, because rendering one to compare it is not always faithful; a change to a partial filter
    ///     needs a manual drop.
    /// </remarks>
    private static bool Matches<TDocument, TKey>(BsonDocument current, MongoIndex<TDocument, TKey> desired)
        where TDocument : IDocument<TKey>
        where TKey : notnull
    {
        if (current.GetValue("key", null) is not BsonDocument currentKeys)
            return false;

        if (!currentKeys.Equals(desired.RenderKeys()))
            return false;

        if (current.GetValue("unique", BsonBoolean.False).ToBoolean() != (desired.Options.Unique ?? false))
            return false;

        if (current.GetValue("sparse", BsonBoolean.False).ToBoolean() != (desired.Options.Sparse ?? false))
            return false;

        var currentTtl = current.TryGetValue("expireAfterSeconds", out var ttl)
            ? ttl.ToDouble()
            : (double?)null;
        var desiredTtl = desired.Options.ExpireAfter?.TotalSeconds;

        return Nullable.Equals(currentTtl, desiredTtl);
    }

    private static (IMongoClient, string) KeyFor<TDocument>(IMongoCollection<TDocument> collection)
        => (collection.Database.Client, collection.CollectionNamespace.FullName);
}
