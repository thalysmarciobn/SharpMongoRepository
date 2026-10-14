using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson.Serialization.IdGenerators;
using MongoDB.Driver;
using SharpMongoRepository;
using SharpMongoRepository.Extensions;
using SharpMongoRepository.Indexing;
using Xunit;

namespace Tests.Unit;

/// <summary>
///     Covers configuration and wiring: the pieces that were unreachable or duplicated before.
/// </summary>
public class OptionsAndRegistrationTests
{
    private const string ConnectionString = "mongodb://localhost:27017";

    [Fact]
    public void Configure_delegate_can_assign_every_option()
    {
        // The whole point: MongoRepositoryOptions used to expose init-only properties, so this delegate
        // body was a compile error (CS8852) and OperationTimeout could not be set at all.
        var options = new MongoRepositoryOptions<Order, Guid>();

        Action<MongoRepositoryOptions<Order, Guid>> configure = o =>
        {
            o.OperationTimeout = TimeSpan.FromSeconds(5);
            o.WriteConcern = WriteConcern.WMajority;
            o.ReadConcern = ReadConcern.Majority;
            o.ReadPreference = ReadPreference.SecondaryPreferred;
            o.IdGenerator = CombGuidGenerator.Instance;
            o.IndexCreation = IndexCreationMode.FirstUse;
            o.RecreateChangedIndexes = true;
        };

        configure(options);

        Assert.Equal(TimeSpan.FromSeconds(5), options.OperationTimeout);
        Assert.Equal(WriteConcern.WMajority, options.WriteConcern);
        Assert.Equal(ReadConcern.Majority, options.ReadConcern);
        Assert.Equal(ReadPreference.SecondaryPreferred, options.ReadPreference);
        Assert.IsType<CombGuidGenerator>(options.IdGenerator);
        Assert.Equal(IndexCreationMode.FirstUse, options.IndexCreation);
        Assert.True(options.RecreateChangedIndexes);
    }

    [Fact]
    public void Default_options_impose_no_timeout_and_no_concerns()
    {
        var options = new MongoRepositoryOptions<Order, Guid>();

        // A 30-second default used to be turned into a WriteConcern carrying only wtimeout, which
        // replaced whatever the connection string asked for.
        Assert.Null(options.OperationTimeout);
        Assert.Null(options.WriteConcern);
        Assert.Null(options.ReadConcern);
        Assert.Null(options.ReadPreference);
        Assert.Empty(options.Indexes);
        Assert.Equal(IndexCreationMode.Startup, options.IndexCreation);
    }

    [Fact]
    public void Connection_string_write_concern_survives_when_options_do_not_set_one()
    {
        var client = new MongoClient(
            MongoClientSettings.FromConnectionString(
                "mongodb://localhost:27017/?w=majority&journal=true"));

        var repository = new MongoRepository<Order, Guid>(client.GetDatabase("test"));

        // This is the regression that mattered most: durability was being silently downgraded.
        Assert.Equal(WriteConcern.WMajority.W, repository.Collection.Settings.WriteConcern.W);
        Assert.True(repository.Collection.Settings.WriteConcern.Journal);
    }

    [Fact]
    public void Configured_write_concern_overrides_the_connection_string()
    {
        var client = new MongoClient(
            MongoClientSettings.FromConnectionString("mongodb://localhost:27017/?w=1"));

        var repository = new MongoRepository<Order, Guid>(
            client.GetDatabase("test"),
            new MongoRepositoryOptions<Order, Guid> { WriteConcern = WriteConcern.WMajority });

        Assert.Equal(WriteConcern.WMajority.W, repository.Collection.Settings.WriteConcern.W);
    }

    [Fact]
    public void Configured_read_preference_reaches_the_collection()
    {
        var client = new MongoClient(MongoClientSettings.FromConnectionString(ConnectionString));

        var repository = new MongoRepository<Order, Guid>(
            client.GetDatabase("test"),
            new MongoRepositoryOptions<Order, Guid>
            {
                ReadPreference = ReadPreference.SecondaryPreferred
            });

        Assert.Equal(
            ReadPreferenceMode.SecondaryPreferred,
            repository.Collection.Settings.ReadPreference!.ReadPreferenceMode);
    }

    [Fact]
    public void Collection_name_comes_from_the_attribute()
    {
        Assert.Equal("orders", MongoRepository<Order, Guid>.CollectionName);
        Assert.Equal("tickets", MongoRepository<Ticket, string>.CollectionName);
    }

    [Fact]
    public void Missing_collection_attribute_fails_with_an_actionable_message()
    {
        var client = new MongoClient(MongoClientSettings.FromConnectionString(ConnectionString));

        // The static initialiser surfaces as a TypeInitializationException on first touch.
        var exception = Record.Exception(
            () => new MongoRepository<Undeclared, Guid>(client.GetDatabase("test")));

        Assert.NotNull(exception);

        var repositoryException = exception as RepositoryException
                                  ?? exception.InnerException as RepositoryException;

        Assert.NotNull(repositoryException);
        Assert.Contains("[BsonCollection]", repositoryException.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Int_keyed_document_can_be_constructed()
    {
        var client = new MongoClient(MongoClientSettings.FromConnectionString(ConnectionString));

        // Previously the constructor threw for any key type without a generator, which made an int key
        // unusable even when the caller always supplies the identifier.
        var repository = new MongoRepository<Counter, int>(client.GetDatabase("test"));

        Assert.Equal("counters", repository.Collection.CollectionNamespace.CollectionName);
    }

    [Fact]
    public void Repository_resolves_from_the_container_with_its_options_applied()
    {
        var services = new ServiceCollection();

        services.AddMongoRepository<Order, Guid>(
            ConnectionString,
            "test",
            options => options
                .AddIndex(MongoIndexes<Order, Guid>.Ascending(x => x.CustomerEmail, unique: true)));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var repository = scope.ServiceProvider.GetRequiredService<IMongoRepository<Order, Guid>>();

        Assert.Equal("orders", repository.Collection.CollectionNamespace.CollectionName);
        Assert.Equal("test", repository.Collection.Database.DatabaseNamespace.DatabaseName);
    }

    [Fact]
    public void One_client_is_shared_by_every_repository_on_the_same_connection_string()
    {
        var services = new ServiceCollection();

        services.AddMongoRepository<Order, Guid>(ConnectionString, "test");
        services.AddMongoRepository<Ticket, string>(ConnectionString, "test");
        services.AddMongoRepository<Event, MongoDB.Bson.ObjectId>(ConnectionString, "other");

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var orders = scope.ServiceProvider.GetRequiredService<IMongoRepository<Order, Guid>>();
        var tickets = scope.ServiceProvider.GetRequiredService<IMongoRepository<Ticket, string>>();
        var events = scope.ServiceProvider.GetRequiredService<IMongoRepository<Event, MongoDB.Bson.ObjectId>>();

        // One connection pool for the whole application, which is also what makes a transaction able to
        // span these collections. Previously each repository instance built its own client.
        Assert.Same(orders.Collection.Database.Client, tickets.Collection.Database.Client);
        Assert.Same(orders.Collection.Database.Client, events.Collection.Database.Client);
    }

    [Fact]
    public void Separate_connection_strings_get_separate_clients()
    {
        var services = new ServiceCollection();

        services.AddMongoRepository<Order, Guid>("mongodb://localhost:27017", "test");
        services.AddMongoRepository<Ticket, string>("mongodb://localhost:27018", "test");

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var orders = scope.ServiceProvider.GetRequiredService<IMongoRepository<Order, Guid>>();
        var tickets = scope.ServiceProvider.GetRequiredService<IMongoRepository<Ticket, string>>();

        Assert.NotSame(orders.Collection.Database.Client, tickets.Collection.Database.Client);
    }

    [Fact]
    public void Repeated_scope_resolution_reuses_the_singleton_client()
    {
        var services = new ServiceCollection();
        services.AddMongoRepository<Order, Guid>(ConnectionString, "test");

        using var provider = services.BuildServiceProvider();

        IMongoClient first;
        IMongoClient second;

        using (var scope = provider.CreateScope())
            first = scope.ServiceProvider
                .GetRequiredService<IMongoRepository<Order, Guid>>().Collection.Database.Client;

        using (var scope = provider.CreateScope())
            second = scope.ServiceProvider
                .GetRequiredService<IMongoRepository<Order, Guid>>().Collection.Database.Client;

        Assert.Same(first, second);
    }

    [Fact]
    public void Singleton_lifetime_is_supported()
    {
        var services = new ServiceCollection();

        services.AddMongoRepository<Order, Guid>(
            ConnectionString, "test", lifetime: ServiceLifetime.Singleton);

        using var provider = services.BuildServiceProvider();

        Assert.Same(
            provider.GetRequiredService<IMongoRepository<Order, Guid>>(),
            provider.GetRequiredService<IMongoRepository<Order, Guid>>());
    }

    [Fact]
    public void Client_settings_hook_is_applied()
    {
        var services = new ServiceCollection();

        services.AddMongoRepository<Order, Guid>(
            ConnectionString,
            "test",
            configureClient: settings => settings.MaxConnectionPoolSize = 42);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var repository = scope.ServiceProvider.GetRequiredService<IMongoRepository<Order, Guid>>();

        Assert.Equal(42, repository.Collection.Database.Client.Settings.MaxConnectionPoolSize);
    }

    [Fact]
    public void Registration_rejects_an_empty_connection_string()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(
            () => services.AddMongoRepository<Order, Guid>("   ", "test"));
    }

    [Fact]
    public void Registration_rejects_an_empty_database_name()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(
            () => services.AddMongoRepository<Order, Guid>(ConnectionString, ""));
    }

    [Fact]
    public void Connection_string_constructor_rejects_an_unparsable_value()
        => Assert.Throws<RepositoryException>(
            () => new MongoRepository<Order, Guid>("not-a-mongodb-uri", "test"));

    [Fact]
    public void Repository_over_a_registered_database_resolves()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IMongoClient>(
            _ => new MongoClient(MongoClientSettings.FromConnectionString(ConnectionString)));
        services.AddSingleton(sp => sp.GetRequiredService<IMongoClient>().GetDatabase("injected"));
        services.AddMongoRepository<Order, Guid>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var repository = scope.ServiceProvider.GetRequiredService<IMongoRepository<Order, Guid>>();

        Assert.Equal("injected", repository.Collection.Database.DatabaseNamespace.DatabaseName);
    }
}
