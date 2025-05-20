using Microsoft.Extensions.Options;
using SharpMongoRepository;
using SharpMongoRepository.Configuration;
using SharpMongoRepository.Interface;

namespace API.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMongoRepository<T>(this IServiceCollection services,
        List<MongoIndex<T>>? indexes) where T : class, IDocument
    {
        services.AddScoped<IMongoRepository<T>>(provider =>
        {
            var settings = provider.GetRequiredService<IOptions<MongoSettings>>().Value;

            var options = new MongoRepositoryOptions<T>
            {
                Indexes = indexes
            };

            return new MongoRepository<T>(settings.ConnectionString, settings.Database, options);
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