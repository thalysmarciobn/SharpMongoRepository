using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using MongoDB.Driver.Core.Configuration;
using SharpMongoRepository.Indexing;
using SharpMongoRepository.Internal;

namespace SharpMongoRepository.Extensions;

/// <summary>
///     Registration helpers for <see cref="IMongoRepository{TDocument,TKey}" />.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Registers a repository, sharing one <see cref="IMongoClient" /> per connection string.
    /// </summary>
    /// <typeparam name="TDocument">Document type, which must implement <see cref="IDocument{TKey}" />.</typeparam>
    /// <typeparam name="TKey">Type of the document's primary key.</typeparam>
    /// <param name="services">Service collection to add to.</param>
    /// <param name="connectionString">MongoDB connection string.</param>
    /// <param name="databaseName">Name of the database.</param>
    /// <param name="configure">
    ///     Configures indexes, concerns, the identifier generator and the operation timeout. Unlike the
    ///     previous version, this delegate can actually assign: the options type no longer uses
    ///     <c>init</c>-only properties, which made every assignment inside it a compile error and left
    ///     <see cref="MongoRepositoryOptions{TDocument,TKey}.OperationTimeout" /> unreachable.
    /// </param>
    /// <param name="configureClient">
    ///     Adjusts the <see cref="MongoClientSettings" /> built from the connection string: pool sizes,
    ///     compressors, server API version. Applied when the client for this connection string is first
    ///     created, so keep it consistent across registrations that share a connection string.
    /// </param>
    /// <param name="lifetime">
    ///     Lifetime of the repository. <see cref="ServiceLifetime.Scoped" /> by default;
    ///     <see cref="ServiceLifetime.Singleton" /> is also safe, because the repository is stateless and the
    ///     driver is thread-safe.
    /// </param>
    /// <returns>The same service collection, so calls can be chained.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services" /> is null.</exception>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="connectionString" /> or <paramref name="databaseName" /> is empty.
    /// </exception>
    /// <remarks>
    ///     <para>
    ///         The client is registered as a keyed singleton under the connection string, so every repository
    ///         pointed at the same cluster shares one connection pool, and a session started from one can
    ///         span the others in a single transaction. The previous version built a client per repository
    ///         instance, which with a scoped registration meant a client per request per document type.
    ///     </para>
    ///     <para>
    ///         When an <see cref="ILoggerFactory" /> is present it is handed to the driver, so cluster,
    ///         connection and command events land in the application's own logging pipeline.
    ///     </para>
    /// </remarks>
    /// <example>
    ///     <code>
    ///     builder.Services.AddMongoRepository&lt;Order, Guid&gt;(
    ///         connectionString,
    ///         databaseName,
    ///         options =&gt; options
    ///             .AddIndex(MongoIndexes&lt;Order, Guid&gt;.Ascending(x =&gt; x.Customer))
    ///             .AddIndex(MongoIndexes&lt;Order, Guid&gt;.TimeToLive(x =&gt; x.CreatedAt, TimeSpan.FromDays(90))));
    ///     </code>
    /// </example>
    public static IServiceCollection AddMongoRepository<TDocument, TKey>(
        this IServiceCollection services,
        string connectionString,
        string databaseName,
        Action<MongoRepositoryOptions<TDocument, TKey>>? configure = null,
        Action<MongoClientSettings>? configureClient = null,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TDocument : IDocument<TKey>
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);

        AddMongoClient(services, connectionString, configureClient);

        return AddRepository<TDocument, TKey>(
            services,
            provider => provider
                .GetRequiredKeyedService<IMongoClient>(connectionString)
                .GetDatabase(databaseName),
            configure,
            lifetime);
    }

    /// <summary>
    ///     Registers a repository over an <see cref="IMongoDatabase" /> already in the container.
    /// </summary>
    /// <typeparam name="TDocument">Document type, which must implement <see cref="IDocument{TKey}" />.</typeparam>
    /// <typeparam name="TKey">Type of the document's primary key.</typeparam>
    /// <param name="services">Service collection to add to.</param>
    /// <param name="configure">Configures indexes, concerns, the identifier generator and the timeout.</param>
    /// <param name="lifetime">Lifetime of the repository.</param>
    /// <returns>The same service collection, so calls can be chained.</returns>
    /// <remarks>
    ///     Use this when the client and database are wired up elsewhere, for instance against a secret store
    ///     or a connection string resolved at run time.
    /// </remarks>
    public static IServiceCollection AddMongoRepository<TDocument, TKey>(
        this IServiceCollection services,
        Action<MongoRepositoryOptions<TDocument, TKey>>? configure = null,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TDocument : IDocument<TKey>
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(services);

        return AddRepository<TDocument, TKey>(
            services,
            provider => provider.GetRequiredService<IMongoDatabase>(),
            configure,
            lifetime);
    }

    /// <summary>
    ///     Registers a repository over a database resolved from the container at run time.
    /// </summary>
    /// <typeparam name="TDocument">Document type, which must implement <see cref="IDocument{TKey}" />.</typeparam>
    /// <typeparam name="TKey">Type of the document's primary key.</typeparam>
    /// <param name="services">Service collection to add to.</param>
    /// <param name="databaseFactory">Resolves the database for this repository.</param>
    /// <param name="configure">Configures indexes, concerns, the identifier generator and the timeout.</param>
    /// <param name="lifetime">Lifetime of the repository.</param>
    /// <returns>The same service collection, so calls can be chained.</returns>
    public static IServiceCollection AddMongoRepository<TDocument, TKey>(
        this IServiceCollection services,
        Func<IServiceProvider, IMongoDatabase> databaseFactory,
        Action<MongoRepositoryOptions<TDocument, TKey>>? configure = null,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TDocument : IDocument<TKey>
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(databaseFactory);

        return AddRepository(services, databaseFactory, configure, lifetime);
    }

    /// <summary>
    ///     Registers a singleton <see cref="IMongoClient" />, keyed by its connection string.
    /// </summary>
    /// <param name="services">Service collection to add to.</param>
    /// <param name="connectionString">MongoDB connection string, which doubles as the service key.</param>
    /// <param name="configureClient">Adjusts the settings built from the connection string.</param>
    /// <returns>The same service collection, so calls can be chained.</returns>
    /// <remarks>
    ///     Registered with <c>TryAdd</c> semantics, so calling it repeatedly for one connection string is
    ///     harmless and the first registration's <paramref name="configureClient" /> is the one that applies.
    ///     The unkeyed <see cref="IMongoClient" /> is also registered, resolving to the first client added,
    ///     which keeps single-database applications simple.
    /// </remarks>
    public static IServiceCollection AddMongoClient(
        this IServiceCollection services,
        string connectionString,
        Action<MongoClientSettings>? configureClient = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.TryAddKeyedSingleton<IMongoClient>(connectionString, (provider, key) =>
        {
            MongoClientSettings settings;
            try
            {
                settings = MongoClientSettings.FromConnectionString((string)key!);
            }
            catch (Exception exception)
            {
                throw new RepositoryException(
                    "The MongoDB connection string could not be parsed.", exception);
            }

            // Route driver diagnostics into the application's logging pipeline when one exists.
            if (settings.LoggingSettings is null &&
                provider.GetService<ILoggerFactory>() is { } loggerFactory)
                settings.LoggingSettings = new LoggingSettings(loggerFactory);

            configureClient?.Invoke(settings);

            return new MongoClient(settings);
        });

        services.TryAddSingleton<IMongoClient>(provider =>
            provider.GetRequiredKeyedService<IMongoClient>(connectionString));

        return services;
    }

    private static IServiceCollection AddRepository<TDocument, TKey>(
        IServiceCollection services,
        Func<IServiceProvider, IMongoDatabase> databaseFactory,
        Action<MongoRepositoryOptions<TDocument, TKey>>? configure,
        ServiceLifetime lifetime)
        where TDocument : IDocument<TKey>
        where TKey : notnull
    {
        var options = new MongoRepositoryOptions<TDocument, TKey>();
        configure?.Invoke(options);

        // Shared by every repository instance, so index creation happens once per process rather than
        // once per scope.
        services.TryAddSingleton<MongoIndexEnsurer>();

        services.Add(new ServiceDescriptor(
            typeof(IMongoRepository<TDocument, TKey>),
            provider => new MongoRepository<TDocument, TKey>(
                databaseFactory(provider),
                options,
                provider.GetService<ILoggerFactory>()?.CreateLogger<MongoRepository<TDocument, TKey>>(),
                provider.GetRequiredService<MongoIndexEnsurer>()),
            lifetime));

        if (options.IndexCreation != IndexCreationMode.Startup || options.Indexes.Count == 0)
            return services;

        services.AddSingleton<IMongoIndexInitializer>(provider =>
            new MongoIndexInitializer<TDocument, TKey>(
                new MongoRepository<TDocument, TKey>(
                    databaseFactory(provider),
                    options,
                    provider.GetService<ILoggerFactory>()
                        ?.CreateLogger<MongoRepository<TDocument, TKey>>(),
                    provider.GetRequiredService<MongoIndexEnsurer>())));

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<Microsoft.Extensions.Hosting.IHostedService, MongoIndexStartupService>());

        return services;
    }

    /// <summary>
    ///     Obsolete. Use the overload that takes a <c>configure</c> delegate.
    /// </summary>
    /// <typeparam name="T">Document type.</typeparam>
    /// <typeparam name="TKey">Type of the document's primary key.</typeparam>
    /// <param name="services">Service collection to add to.</param>
    /// <param name="connectionString">MongoDB connection string.</param>
    /// <param name="database">Name of the database.</param>
    /// <param name="indexes">Indexes to create.</param>
    /// <param name="configureOptions">Further configuration.</param>
    /// <returns>The same service collection, so calls can be chained.</returns>
    [Obsolete("Pass indexes through the configure delegate: " +
              "AddMongoRepository<T, TKey>(connectionString, database, o => o.AddIndex(...)). " +
              "This overload will be removed in 0.1.0.")]
    public static IServiceCollection AddMongoRepository<T, TKey>(
        this IServiceCollection services,
        string connectionString,
        string database,
        List<MongoIndex<T, TKey>>? indexes,
        Action<MongoRepositoryOptions<T, TKey>>? configureOptions = null)
        where T : class, IDocument<TKey>
        where TKey : notnull
        => services.AddMongoRepository<T, TKey>(connectionString, database, options =>
        {
            if (indexes is not null)
                foreach (var index in indexes)
                    options.AddIndex(index);

            configureOptions?.Invoke(options);
        });
}
