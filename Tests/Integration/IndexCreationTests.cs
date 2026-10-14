using MongoDB.Bson;
using MongoDB.Driver;
using SharpMongoRepository;
using SharpMongoRepository.Indexing;
using Xunit;

namespace Tests.Integration;

/// <summary>
///     Verifies that configured indexes actually reach the server, in the shape that was configured.
/// </summary>
[Collection(MongoCollectionFixture.Name)]
[Trait("Category", "Integration")]
public class IndexCreationTests(MongoFixture fixture) : IAsyncLifetime
{
    private readonly string _database = MongoFixture.NewDatabaseName();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => fixture.CreateClient().DropDatabaseAsync(_database);

    private MongoRepository<Order, Guid> Build(
        Action<MongoRepositoryOptions<Order, Guid>> configure)
    {
        var options = new MongoRepositoryOptions<Order, Guid> { IndexCreation = IndexCreationMode.FirstUse };
        configure(options);

        return new MongoRepository<Order, Guid>(
            fixture.CreateClient().GetDatabase(_database), options);
    }

    private static async Task<List<BsonDocument>> ListIndexesAsync(
        MongoRepository<Order, Guid> repository)
    {
        var cursor = await repository.Collection.Indexes.ListAsync();
        return await cursor.ToListAsync();
    }

    [Fact]
    public async Task Configured_index_is_created_with_the_expected_name_and_keys()
    {
        var repository = Build(o => o.AddIndex(
            MongoIndexes<Order, Guid>.Ascending(x => x.CustomerEmail, unique: true)));

        await repository.EnsureIndexesAsync();

        var indexes = await ListIndexesAsync(repository);
        var index = indexes.Single(i => i["name"] == "customer_email_1");

        Assert.Equal(new BsonDocument("customer_email", 1), index["key"].AsBsonDocument);
        Assert.True(index.GetValue("unique", false).ToBoolean());
    }

    [Fact]
    public async Task Ascending_and_descending_indexes_on_one_field_both_get_created()
    {
        // Previously both were named after the field alone, so the ensurer treated the second as already
        // present and created only one.
        var repository = Build(o => o.AddIndexes(
            MongoIndexes<Order, Guid>.Ascending(x => x.Total),
            MongoIndexes<Order, Guid>.Descending(x => x.Total)));

        await repository.EnsureIndexesAsync();

        var names = (await ListIndexesAsync(repository)).Select(i => i["name"].AsString).ToList();

        Assert.Contains("Total_1", names);
        Assert.Contains("Total_-1", names);
    }

    [Fact]
    public async Task Nested_field_index_is_created_on_the_full_path()
    {
        var repository = Build(o => o.AddIndex(
            MongoIndexes<Order, Guid>.Ascending(x => x.Address!.City)));

        await repository.EnsureIndexesAsync();

        var index = (await ListIndexesAsync(repository)).Single(i => i["name"] == "Address.city_1");

        // The old implementation created { "City": 1 }, an index on a field no document has.
        Assert.Equal(new BsonDocument("Address.city", 1), index["key"].AsBsonDocument);
    }

    [Fact]
    public async Task Compound_index_keeps_its_declared_order_and_directions()
    {
        var repository = Build(o => o.AddIndex(MongoIndexes<Order, Guid>.Compound(
            MongoIndexes<Order, Guid>.Field(IndexDirection.Ascending, x => x.Status),
            MongoIndexes<Order, Guid>.Field(IndexDirection.Descending, x => x.CreatedAt))));

        await repository.EnsureIndexesAsync();

        var index = (await ListIndexesAsync(repository))
            .Single(i => i["name"] == "Status_1_CreatedAt_-1");

        var expected = new BsonDocument { { "Status", 1 }, { "CreatedAt", -1 } };

        Assert.Equal(expected, index["key"].AsBsonDocument);
    }

    [Fact]
    public async Task Ttl_index_carries_its_expiry()
    {
        var repository = Build(o => o.AddIndex(
            MongoIndexes<Order, Guid>.TimeToLive(x => x.CreatedAt, TimeSpan.FromMinutes(5))));

        await repository.EnsureIndexesAsync();

        var index = (await ListIndexesAsync(repository)).Single(i => i["name"] == "CreatedAt_1");

        Assert.Equal(300, index["expireAfterSeconds"].ToDouble());
    }

    [Fact]
    public async Task Unique_index_is_enforced_by_the_server()
    {
        var repository = Build(o => o.AddIndex(
            MongoIndexes<Order, Guid>.Ascending(x => x.CustomerEmail, unique: true)));

        await repository.InsertOneAsync(new Order { CustomerEmail = "dup@example.com" });

        // A duplicate key stays a driver exception rather than being wrapped, so the caller can read the
        // server error code and decide whether to retry.
        var exception = await Assert.ThrowsAsync<MongoWriteException>(
            () => repository.InsertOneAsync(new Order { CustomerEmail = "dup@example.com" }));

        Assert.Equal(ServerErrorCategory.DuplicateKey, exception.WriteError.Category);
    }

    [Fact]
    public async Task Ensure_is_idempotent_and_does_nothing_on_a_second_call()
    {
        var repository = Build(o => o.AddIndex(MongoIndexes<Order, Guid>.Ascending(x => x.Total)));

        await repository.EnsureIndexesAsync();
        var first = (await ListIndexesAsync(repository)).Count;

        await repository.EnsureIndexesAsync();
        var second = (await ListIndexesAsync(repository)).Count;

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task A_changed_definition_is_left_alone_by_default()
    {
        var shared = fixture.CreateClient().GetDatabase(_database);

        var before = new MongoRepository<Order, Guid>(shared, new MongoRepositoryOptions<Order, Guid>
        {
            IndexCreation = IndexCreationMode.FirstUse
        }.AddIndex(MongoIndexes<Order, Guid>.Ascending(
            x => x.Status, configure: o => o.Name = "drifting")));

        await before.EnsureIndexesAsync();

        // Same name, different keys. A separate ensurer instance, so the "already done" cache does not
        // hide the comparison.
        var after = new MongoRepository<Order, Guid>(shared, new MongoRepositoryOptions<Order, Guid>
        {
            IndexCreation = IndexCreationMode.FirstUse
        }.AddIndex(MongoIndexes<Order, Guid>.Ascending(
            x => x.Total, configure: o => o.Name = "drifting")));

        await after.EnsureIndexesAsync();

        var index = (await ListIndexesAsync(after)).Single(i => i["name"] == "drifting");

        Assert.Equal(new BsonDocument("Status", 1), index["key"].AsBsonDocument);
    }

    [Fact]
    public async Task A_changed_definition_is_rebuilt_when_that_is_asked_for()
    {
        var shared = fixture.CreateClient().GetDatabase(_database);

        var before = new MongoRepository<Order, Guid>(shared, new MongoRepositoryOptions<Order, Guid>
        {
            IndexCreation = IndexCreationMode.FirstUse
        }.AddIndex(MongoIndexes<Order, Guid>.Ascending(
            x => x.Status, configure: o => o.Name = "rebuilt")));

        await before.EnsureIndexesAsync();

        var after = new MongoRepository<Order, Guid>(shared, new MongoRepositoryOptions<Order, Guid>
        {
            IndexCreation = IndexCreationMode.FirstUse,
            RecreateChangedIndexes = true
        }.AddIndex(MongoIndexes<Order, Guid>.Ascending(
            x => x.Total, configure: o => o.Name = "rebuilt")));

        await after.EnsureIndexesAsync();

        var index = (await ListIndexesAsync(after)).Single(i => i["name"] == "rebuilt");

        Assert.Equal(new BsonDocument("Total", 1), index["key"].AsBsonDocument);
    }

    [Fact]
    public async Task Disabled_mode_creates_nothing()
    {
        var repository = new MongoRepository<Order, Guid>(
            fixture.CreateClient().GetDatabase(_database),
            new MongoRepositoryOptions<Order, Guid> { IndexCreation = IndexCreationMode.Disabled }
                .AddIndex(MongoIndexes<Order, Guid>.Ascending(x => x.Total)));

        await repository.InsertOneAsync(new Order());
        await repository.EnsureIndexesAsync();

        var names = (await ListIndexesAsync(repository)).Select(i => i["name"].AsString);

        Assert.Equal(["_id_"], names);
    }

    [Fact]
    public async Task First_use_mode_creates_indexes_before_the_first_operation_completes()
    {
        var repository = Build(o => o.AddIndex(MongoIndexes<Order, Guid>.Ascending(x => x.Total)));

        await repository.InsertOneAsync(new Order { Total = 1 });

        var names = (await ListIndexesAsync(repository)).Select(i => i["name"].AsString);

        Assert.Contains("Total_1", names);
    }
}
