using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MongoDB.Bson;
using MongoDB.Driver;
using SharpMongoRepository;
using SharpMongoRepository.Extensions;
using SharpMongoRepository.Indexing;
using Xunit;

namespace Tests.Integration;

/// <summary>
///     Covers transactions across collections, the key types that used to be broken, and the hosted
///     start-up path that creates indexes before the first request.
/// </summary>
[Collection(MongoCollectionFixture.Name)]
[Trait("Category", "Integration")]
public class TransactionAndKeyTests(MongoFixture fixture) : IAsyncLifetime
{
    private readonly string _database = MongoFixture.NewDatabaseName();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => fixture.CreateClient().DropDatabaseAsync(_database);

    [Fact]
    public async Task String_keyed_insert_many_generates_distinct_keys()
    {
        // The driver registers no identifier generator for a string key, and the old implementation only
        // generated keys on the single-document path. This wrote _id: null three times and failed.
        var tickets = new MongoRepository<Ticket, string>(
            fixture.CreateClient().GetDatabase(_database));

        var batch = new List<Ticket>
        {
            new() { Subject = "one" },
            new() { Subject = "two" },
            new() { Subject = "three" }
        };

        await tickets.InsertManyAsync(batch);

        Assert.All(batch, ticket => Assert.NotEmpty(ticket.Id));
        Assert.Equal(3, await tickets.CountAsync());

        var found = await tickets.FindByIdAsync(batch[1].Id);

        Assert.NotNull(found);
        Assert.Equal("two", found.Subject);
    }

    [Fact]
    public async Task ObjectId_keyed_documents_round_trip()
    {
        var events = new MongoRepository<Event, ObjectId>(
            fixture.CreateClient().GetDatabase(_database));

        var @event = new Event { Name = "created" };
        await events.InsertOneAsync(@event);

        Assert.NotEqual(ObjectId.Empty, @event.Id);

        var found = await events.FindByIdAsync(@event.Id);

        Assert.NotNull(found);
        Assert.Equal("created", found.Name);
    }

    [Fact]
    public async Task Int_keyed_documents_work_when_the_caller_supplies_the_key()
    {
        // Previously impossible: the constructor threw for any key type without a generator.
        var counters = new MongoRepository<Counter, int>(
            fixture.CreateClient().GetDatabase(_database));

        await counters.InsertManyAsync([
            new Counter { Id = 1, Value = 10 },
            new Counter { Id = 2, Value = 20 }
        ]);

        var found = await counters.FindByIdAsync(2);

        Assert.NotNull(found);
        Assert.Equal(20, found.Value);
    }

    [Fact]
    public async Task Comb_guid_generator_can_be_configured_for_a_repository()
    {
        var orders = new MongoRepository<Order, Guid>(
            fixture.CreateClient().GetDatabase(_database),
            new MongoRepositoryOptions<Order, Guid>
            {
                IdGenerator = MongoDB.Bson.Serialization.IdGenerators.CombGuidGenerator.Instance
            });

        var batch = Enumerable.Range(0, 5).Select(_ => new Order()).ToList();
        await orders.InsertManyAsync(batch);

        Assert.All(batch, order => Assert.NotEqual(Guid.Empty, order.Id));
        Assert.Equal(5, await orders.CountAsync());
    }

    [Fact]
    public async Task Transaction_commits_every_write()
    {
        var orders = new MongoRepository<Order, Guid>(
            fixture.CreateClient().GetDatabase(_database));

        var inserted = await orders.WithTransactionAsync(async (session, token) =>
        {
            await orders.InsertOneAsync(new Order { Total = 1 }, session, token);
            await orders.InsertOneAsync(new Order { Total = 2 }, session, token);
            return 2;
        });

        Assert.Equal(2, inserted);
        Assert.Equal(2, await orders.CountAsync());
    }

    [Fact]
    public async Task Transaction_rolls_back_on_failure()
    {
        var orders = new MongoRepository<Order, Guid>(
            fixture.CreateClient().GetDatabase(_database));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            orders.WithTransactionAsync(async (session, token) =>
            {
                await orders.InsertOneAsync(new Order { Total = 1 }, session, token);
                throw new InvalidOperationException("abort");
            }));

        Assert.Equal(0, await orders.CountAsync());
    }

    [Fact]
    public async Task Void_returning_transaction_overload_commits()
    {
        var orders = new MongoRepository<Order, Guid>(
            fixture.CreateClient().GetDatabase(_database));

        await orders.WithTransactionAsync(async (session, token) =>
            await orders.InsertOneAsync(new Order { Total = 5 }, session, token));

        Assert.Equal(1, await orders.CountAsync());
    }

    [Fact]
    public async Task A_session_spans_two_collections_in_one_transaction()
    {
        // Only possible because every repository registered against one connection string shares a
        // single IMongoClient. The old per-repository client made this unreachable.
        var services = new ServiceCollection();
        services.AddMongoRepository<Order, Guid>(fixture.ConnectionString, _database);
        services.AddMongoRepository<Ticket, string>(fixture.ConnectionString, _database);

        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var orders = scope.ServiceProvider.GetRequiredService<IMongoRepository<Order, Guid>>();
        var tickets = scope.ServiceProvider.GetRequiredService<IMongoRepository<Ticket, string>>();

        Assert.Same(orders.Collection.Database.Client, tickets.Collection.Database.Client);

        using var session = await orders.StartSessionAsync();

        await session.WithTransactionAsync(async (handle, token) =>
        {
            await orders.InsertOneAsync(new Order { Total = 1 }, handle, token);
            await tickets.InsertOneAsync(new Ticket { Subject = "linked" }, handle, token);
            return true;
        });

        Assert.Equal(1, await orders.CountAsync());
        Assert.Equal(1, await tickets.CountAsync());
    }

    [Fact]
    public async Task Cross_collection_transaction_rolls_both_back_on_failure()
    {
        var services = new ServiceCollection();
        services.AddMongoRepository<Order, Guid>(fixture.ConnectionString, _database);
        services.AddMongoRepository<Ticket, string>(fixture.ConnectionString, _database);

        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var orders = scope.ServiceProvider.GetRequiredService<IMongoRepository<Order, Guid>>();
        var tickets = scope.ServiceProvider.GetRequiredService<IMongoRepository<Ticket, string>>();

        using var session = await orders.StartSessionAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.WithTransactionAsync<bool>(async (handle, token) =>
            {
                await orders.InsertOneAsync(new Order { Total = 1 }, handle, token);
                await tickets.InsertOneAsync(new Ticket { Subject = "doomed" }, handle, token);
                throw new InvalidOperationException("abort");
            }));

        Assert.Equal(0, await orders.CountAsync());
        Assert.Equal(0, await tickets.CountAsync());
    }

    [Fact]
    public async Task Hosted_startup_creates_indexes_before_the_host_is_running()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Services.AddMongoRepository<Order, Guid>(
            fixture.ConnectionString,
            _database,
            options => options.AddIndex(
                MongoIndexes<Order, Guid>.Ascending(x => x.CustomerEmail, unique: true)));

        using var host = builder.Build();

        await host.StartAsync();
        try
        {
            using var scope = host.Services.CreateScope();
            var orders = scope.ServiceProvider.GetRequiredService<IMongoRepository<Order, Guid>>();

            var cursor = await orders.Collection.Indexes.ListAsync();
            var names = (await cursor.ToListAsync()).Select(i => i["name"].AsString);

            // The index exists without any request having touched the collection.
            Assert.Contains("customer_email_1", names);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task Startup_failure_against_an_unreachable_server_does_not_stop_the_host()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Services.AddMongoRepository<Order, Guid>(
            "mongodb://127.0.0.1:1/?serverSelectionTimeoutMS=250&connectTimeoutMS=250",
            "unreachable",
            options => options.AddIndex(MongoIndexes<Order, Guid>.Ascending(x => x.Total)));

        using var host = builder.Build();

        // Logged as a warning and retried on first use, rather than taking the process down at boot.
        await host.StartAsync();
        await host.StopAsync();
    }
}
