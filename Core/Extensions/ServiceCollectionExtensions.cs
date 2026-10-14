using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using MongoDB.Driver.Core.Configuration;
using SharpMongoRepository.Indexing;
using SharpMongoRepository.Internal;

namespace SharpMongoRepository.Extensions;

/// <summary>
///     Registration helpers for <see cref="IMongoRepository{TDocument}" /> and
///     <see cref="IMongoRepository{TDocument,TKey}" />.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Registers a keyed repository, sharing one <see cref="IMongoClient" /> per connection string.
    /// </summary>
    /// <typeparam name="TDocument">Document type, which must implement <see cref="IDocument{TKey}" />.</typeparam>
    /// <typeparam name="TKey">Type of the document's primary key.</typeparam>
    /// <param name="services">Service collection to add to.</param>
    /// <param name="connectionString">MongoDB connection string.</param>
    /// <param name="databaseName">Name of the database.</param>
    /// <param name="configure">Configures indexes, concerns, the identifier generator and the timeout.</param>
    /// <param name="configureClient">
    ///     Adjusts the <see cref="MongoClientSettings" /> built from the connection string: pool sizes,
    ///     compressors, server API version. Applied when the client for this connection string is first
    ///     created, so keep it consistent across registrations that share a connection string.
    /// </param>
    /// <param name="lifetime">
    ///     Lifetime of the repository. <see cref="ServiceLifetime.Scoped" /> by default;
    ///     <see cref="ServiceLifetime.Singleton" /> is also safe, because the repository is stateless and
    ///     the driver is thread-safe.
    /// </param>
    /// <returns>The same service collection, so calls can be chained.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services" /> is null.</exception>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="connectionString" /> or <paramref name="databaseName" /> is empty.
    /// </exception>
    /// <remarks>
    ///     <para>
    ///         Registers <see cref="IMongoRepository{TDocument,TKey}" /> and, resolving to the same
    ///         instance, <see cref="IMongoRepository{TDocument}" />, so code that only reads and writes by
    ///         filter can depend on the narrower interface.
    ///     </para>
    ///     <para>
    ///         The client is a keyed singleton under the connection string, so every repository pointed at
    ///         the same cluster shares one connection pool, and a session started from one can span the
    ///         others in a single transaction.
    ///     </para>
    /// </remarks>
    /// <example>
    ///     <code>
    ///     builder.Services.AddMongoRepository&lt;Order, Guid&gt;(
    ///         connectionString,
    ///         databaseName,
    ///         options =&gt; options.AddIndex(MongoIndexes&lt;Order&gt;.Ascending(x =&gt; x.Customer)));
    ///     </code>
    /// </example>
    public static IServiceCollection AddMongoRepository<TDocument, TKey>(
        this IServiceCollection services,
        string connectionString,
        string databaseName,
        Action<MongoRepositoryOptions<TDocument>>? configure = null,
        Action<MongoClientSettings>? configureClient = null,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TDocument : IDocument<TKey>
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);

        AddMongoClient(services, connectionString, configureClient);

        return AddKeyed<TDocument, TKey>(
            services, DatabaseFactory(connectionString, databaseName), configure, lifetime);
    }

    /// <summary>
    ///     Registers a keyed repository over an <see cref="IMongoDatabase" /> already in the container.
    /// </summary>
    /// <typeparam name="TDocument">Document type, which must implement <see cref="IDocument{TKey}" />.</typeparam>
    /// <typeparam name="TKey">Type of the document's primary key.</typeparam>
    /// <param name="services">Service collection to add to.</param>
    /// <param name="configure">Configures indexes, concerns, the identifier generator and the timeout.</param>
    /// <param name="lifetime">Lifetime of the repository.</param>
    /// <returns>The same service collection, so calls can be chained.</returns>
    public static IServiceCollection AddMongoRepository<TDocument, TKey>(
        this IServiceCollection services,
        Action<MongoRepositoryOptions<TDocument>>? configure = null,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TDocument : IDocument<TKey>
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(services);

        return AddKeyed<TDocument, TKey>(
            services, provider => provider.GetRequiredService<IMongoDatabase>(), configure, lifetime);
    }

    /// <summary>
    ///     Registers a keyed repository over a database resolved from the container at run time.
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
        Action<MongoRepositoryOptions<TDocument>>? configure = null,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TDocument : IDocument<TKey>
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(databaseFactory);

        return AddKeyed<TDocument, TKey>(services, databaseFactory, configure, lifetime);
    }

    /// <summary>
    ///     Registers a keyless repository, for a document type with no declared key.
    /// </summary>
    /// <typeparam name="TDocument">Document type. No interface or attribute has to be implemented.</typeparam>
    /// <param name="services">Service collection to add to.</param>
    /// <param name="connectionString">MongoDB connection string.</param>
    /// <param name="databaseName">Name of the database.</param>
    /// <param name="collectionName">
    ///     Collection to use. Required unless the type carries <see cref="BsonCollectionAttribute" />, and
    ///     the way to use a type you do not own, or <c>BsonDocument</c> for a collection with no fixed
    ///     shape.
    /// </param>
    /// <param name="configure">Configures indexes, concerns and the timeout.</param>
    /// <param name="configureClient">Adjusts the settings built from the connection string.</param>
    /// <param name="lifetime">Lifetime of the repository.</param>
    /// <returns>The same service collection, so calls can be chained.</returns>
    /// <remarks>
    ///     Reads are expressed through filters, with <see cref="IMongoRepository{TDocument}.Query" /> as the
    ///     comfortable way to do that. Identifiers are left to the driver and the server.
    /// </remarks>
    /// <example>
    ///     <code>
    ///     // A type that cannot implement IDocument&lt;TKey&gt;, stored in a named collection.
    ///     builder.Services.AddMongoRepository&lt;AuditEntry&gt;(
    ///         connectionString, databaseName, collectionName: "audit");
    ///
    ///     // Or a collection with no fixed shape at all.
    ///     builder.Services.AddMongoRepository&lt;BsonDocument&gt;(
    ///         connectionString, databaseName, collectionName: "events");
    ///     </code>
    /// </example>
    public static IServiceCollection AddMongoRepository<TDocument>(
        this IServiceCollection services,
        string connectionString,
        string databaseName,
        string? collectionName = null,
        Action<MongoRepositoryOptions<TDocument>>? configure = null,
        Action<MongoClientSettings>? configureClient = null,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);

        AddMongoClient(services, connectionString, configureClient);

        return AddKeyless(
            services,
            DatabaseFactory(connectionString, databaseName),
            Combine(collectionName, configure),
            lifetime);
    }

    /// <summary>
    ///     Registers a keyless repository over a database resolved from the container at run time.
    /// </summary>
    /// <typeparam name="TDocument">Document type. No interface or attribute has to be implemented.</typeparam>
    /// <param name="services">Service collection to add to.</param>
    /// <param name="databaseFactory">Resolves the database for this repository.</param>
    /// <param name="collectionName">Collection to use, or <see langword="null" /> to use the attribute.</param>
    /// <param name="configure">Configures indexes, concerns and the timeout.</param>
    /// <param name="lifetime">Lifetime of the repository.</param>
    /// <returns>The same service collection, so calls can be chained.</returns>
    public static IServiceCollection AddMongoRepository<TDocument>(
        this IServiceCollection services,
        Func<IServiceProvider, IMongoDatabase> databaseFactory,
        string? collectionName = null,
        Action<MongoRepositoryOptions<TDocument>>? configure = null,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(databaseFactory);

        return AddKeyless(services, databaseFactory, Combine(collectionName, configure), lifetime);
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
    ///     harmless and the first registration's <paramref name="configureClient" /> is the one that
    ///     applies. The unkeyed <see cref="IMongoClient" /> is also registered, resolving to the first
    ///     client added, which keeps single-database applications simple.
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

    private static Func<IServiceProvider, IMongoDatabase> DatabaseFactory(
        string connectionString,
        string databaseName)
        => provider => provider
            .GetRequiredKeyedService<IMongoClient>(connectionString)
            .GetDatabase(databaseName);

    /// <summary>
    ///     Folds an explicit collection name into the caller's configure delegate.
    /// </summary>
    /// <remarks>
    ///     The name is applied first, so a <paramref name="configure" /> that also sets
    ///     <see cref="MongoRepositoryOptions{TDocument}.CollectionName" /> wins.
    /// </remarks>
    private static Action<MongoRepositoryOptions<TDocument>>? Combine<TDocument>(
        string? collectionName,
        Action<MongoRepositoryOptions<TDocument>>? configure)
    {
        if (string.IsNullOrWhiteSpace(collectionName))
            return configure;

        return options =>
        {
            options.CollectionName = collectionName;
            configure?.Invoke(options);
        };
    }

    private static IServiceCollection AddKeyless<TDocument>(
        IServiceCollection services,
        Func<IServiceProvider, IMongoDatabase> databaseFactory,
        Action<MongoRepositoryOptions<TDocument>>? configure,
        ServiceLifetime lifetime)
    {
        var options = BuildOptions(configure);

        services.Add(new ServiceDescriptor(
            typeof(IMongoRepository<TDocument>),
            provider => new MongoRepository<TDocument>(
                databaseFactory(provider),
                options,
                LoggerFor<TDocument>(provider),
                provider.GetRequiredService<MongoIndexEnsurer>()),
            lifetime));

        return RegisterStartupIndexing(services, databaseFactory, options);
    }

    private static IServiceCollection AddKeyed<TDocument, TKey>(
        IServiceCollection services,
        Func<IServiceProvider, IMongoDatabase> databaseFactory,
        Action<MongoRepositoryOptions<TDocument>>? configure,
        ServiceLifetime lifetime)
        where TDocument : IDocument<TKey>
        where TKey : notnull
    {
        var options = BuildOptions(configure);

        services.Add(new ServiceDescriptor(
            typeof(IMongoRepository<TDocument, TKey>),
            provider => new MongoRepository<TDocument, TKey>(
                databaseFactory(provider),
                options,
                LoggerFor<TDocument>(provider),
                provider.GetRequiredService<MongoIndexEnsurer>()),
            lifetime));

        // The keyless interface resolves to the same instance, so a consumer that only filters can depend
        // on the narrower contract without a second registration or a second client.
        services.Add(new ServiceDescriptor(
            typeof(IMongoRepository<TDocument>),
            provider => provider.GetRequiredService<IMongoRepository<TDocument, TKey>>(),
            lifetime));

        return RegisterStartupIndexing(services, databaseFactory, options);
    }

    private static MongoRepositoryOptions<TDocument> BuildOptions<TDocument>(
        Action<MongoRepositoryOptions<TDocument>>? configure)
    {
        var options = new MongoRepositoryOptions<TDocument>();
        configure?.Invoke(options);
        return options;
    }

    private static ILogger? LoggerFor<TDocument>(IServiceProvider provider)
        => provider.GetService<ILoggerFactory>()?.CreateLogger<MongoRepository<TDocument>>();

    private static IServiceCollection RegisterStartupIndexing<TDocument>(
        IServiceCollection services,
        Func<IServiceProvider, IMongoDatabase> databaseFactory,
        MongoRepositoryOptions<TDocument> options)
    {
        // Shared by every repository instance, so index creation happens once per process rather than
        // once per scope.
        services.TryAddSingleton<MongoIndexEnsurer>();

        if (options.IndexCreation != IndexCreationMode.Startup || options.Indexes.Count == 0)
            return services;

        // Built directly rather than resolved, because a scoped repository cannot be reached from the
        // singleton that runs at start-up. It shares the ensurer, so the work is not repeated when the
        // first request arrives.
        services.AddSingleton<IMongoIndexInitializer>(provider =>
            new MongoIndexInitializer<TDocument>(
                new MongoRepository<TDocument>(
                    databaseFactory(provider),
                    options,
                    LoggerFor<TDocument>(provider),
                    provider.GetRequiredService<MongoIndexEnsurer>())));

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, MongoIndexStartupService>());

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
              "This overload will be removed in 0.2.0.")]
    public static IServiceCollection AddMongoRepository<T, TKey>(
        this IServiceCollection services,
        string connectionString,
        string database,
        List<MongoIndex<T>>? indexes,
        Action<MongoRepositoryOptions<T>>? configureOptions = null)
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
