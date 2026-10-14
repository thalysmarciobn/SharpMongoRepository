using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SharpMongoRepository.Internal;

/// <summary>
///     Creates indexes for one registered document type.
/// </summary>
internal interface IMongoIndexInitializer
{
    /// <summary>Gets the collection the initializer is responsible for, for logging.</summary>
    string CollectionName { get; }

    /// <summary>Creates any missing index on that collection.</summary>
    Task EnsureAsync(CancellationToken cancellationToken);
}

/// <inheritdoc />
internal sealed class MongoIndexInitializer<TDocument, TKey>(IMongoRepository<TDocument, TKey> repository)
    : IMongoIndexInitializer
    where TDocument : IDocument<TKey>
    where TKey : notnull
{
    public string CollectionName => repository.Collection.CollectionNamespace.FullName;

    public Task EnsureAsync(CancellationToken cancellationToken)
        => repository.EnsureIndexesAsync(cancellationToken);
}

/// <summary>
///     Creates every registered collection's indexes once, during application start-up.
/// </summary>
/// <remarks>
///     <para>
///         This is what keeps index management off the request path. The previous version listed and created
///         indexes inside the repository's lazy initialiser, so with a scoped registration every HTTP request
///         paid for a <c>listIndexes</c> round-trip before its first query.
///     </para>
///     <para>
///         A failure here is logged rather than thrown. The application still starts, and because a failed
///         attempt is not recorded as complete, the first operation to touch the collection retries. Letting
///         a transient database outage at boot take the whole process down would be a worse trade.
///     </para>
/// </remarks>
internal sealed class MongoIndexStartupService(
    IEnumerable<IMongoIndexInitializer> initializers,
    ILogger<MongoIndexStartupService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var initializer in initializers)
        {
            try
            {
                await initializer.EnsureAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Could not create indexes for {Collection} during start-up. They will be retried the " +
                    "first time the collection is used.",
                    initializer.CollectionName);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
