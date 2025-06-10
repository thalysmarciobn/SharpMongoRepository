using Microsoft.Extensions.DependencyInjection;
using SharpMongoRepository.Configuration;
using SharpMongoRepository.Interface;

namespace SharpMongoRepository.Extensions;

public static class ServiceCollectionExtensions
{
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