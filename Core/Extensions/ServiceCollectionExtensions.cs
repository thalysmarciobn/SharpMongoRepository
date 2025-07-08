using Microsoft.Extensions.DependencyInjection;
using SharpMongoRepository.Configuration;
using SharpMongoRepository.Interface;

namespace SharpMongoRepository.Extensions;

/// <summary>
/// Provides extension methods for <see cref="IServiceCollection"/> to register MongoDB repositories.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds a MongoDB repository service to the specified <see cref="IServiceCollection"/>.
    /// </summary>
    /// <typeparam name="T">The document entity type that implements <see cref="IDocument{TKey}"/>.</typeparam>
    /// <typeparam name="TKey">The type of the document's primary key.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="connectionString">The MongoDB connection string.</param>
    /// <param name="database">The name of the MongoDB database.</param>
    /// <param name="indexes">Optional list of MongoDB indexes to be created for the collection.</param>
    /// <param name="configureOptions">Optional action to configure additional repository options.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    /// <remarks>
    /// This method registers a scoped <see cref="IMongoRepository{T, TKey}"/> service that provides
    /// CRUD operations for the specified document type in MongoDB.
    /// </remarks>
    public static IServiceCollection AddMongoRepository<T, TKey>(
        this IServiceCollection services,
        string connectionString,
        string database,
        List<MongoIndex<T, TKey>>? indexes = null,
        Action<MongoRepositoryOptions<T, TKey>>? configureOptions = null)
        where T : class, IDocument<TKey>
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IMongoRepository<T, TKey>>(provider =>
        {
            var options = new MongoRepositoryOptions<T, TKey>
            {
                Indexes = indexes
            };

            configureOptions?.Invoke(options);

            return new MongoRepository<T, TKey>(
                connectionString,
                database,
                options);
        });

        return services;
    }
}