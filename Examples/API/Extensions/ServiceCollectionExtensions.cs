using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.IdGenerators;
using SharpMongoRepository;
using SharpMongoRepository.Configuration;
using SharpMongoRepository.Interface;

namespace API.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMongoRepository<T, TKey>(
        this IServiceCollection services,
        List<MongoIndex<T, TKey>>? indexes = null,
        Action<MongoRepositoryOptions<T, TKey>>? configureOptions = null)
        where T : class, IDocument<TKey>
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        services.AddScoped<IMongoRepository<T, TKey>>(provider =>
        {
            var settings = provider.GetRequiredService<IOptions<MongoSettings>>().Value
                ?? throw new InvalidOperationException("MongoDB settings not configured");

            var options = new MongoRepositoryOptions<T, TKey>
            {
                Indexes = indexes
            };

            configureOptions?.Invoke(options);

            return new MongoRepository<T, TKey>(
                settings.ConnectionString,
                settings.Database,
                options);
        });

        return services;
    }

    public static IServiceCollection AddSwaggerDocumentation(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();

        return services;
    }
}