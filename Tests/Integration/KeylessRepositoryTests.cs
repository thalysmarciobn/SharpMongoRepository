using MongoDB.Bson;
using MongoDB.Driver;
using SharpMongoRepository;
using SharpMongoRepository.Indexing;
using Xunit;

namespace Tests.Integration;

/// <summary>
///     Covers the repository over a document type with no key, no interface and, for most of these, no
///     attribute either.
/// </summary>
[Collection(MongoCollectionFixture.Name)]
[Trait("Category", "Integration")]
public class KeylessRepositoryTests(MongoFixture fixture) : IAsyncLifetime
{
    private readonly string _database = MongoFixture.NewDatabaseName();
    private IMongoRepository<AuditEntry> _audit = null!;

    public Task InitializeAsync()
    {
        // AuditEntry implements nothing and carries no attribute; the collection comes from the options.
        _audit = new MongoRepository<AuditEntry>(
            fixture.CreateClient().GetDatabase(_database),
            new MongoRepositoryOptions<AuditEntry>().InCollection("audit"));

        return Task.CompletedTask;
    }

    public Task DisposeAsync() => fixture.CreateClient().DropDatabaseAsync(_database);

    [Fact]
    public async Task A_type_with_no_key_round_trips()
    {
        await _audit.InsertOneAsync(new AuditEntry { Action = "login", Actor = "ana", Severity = 1 });

        var found = await _audit.FindOneAsync(x => x.Actor == "ana");

        Assert.NotNull(found);
        Assert.Equal("login", found.Action);
    }

    [Fact]
    public async Task The_server_still_assigns_an_id_even_with_no_key_member()
    {
        await _audit.InsertOneAsync(new AuditEntry { Action = "logout" });

        var raw = await _audit.Collection.Database
            .GetCollection<BsonDocument>("audit")
            .Find(FilterDefinition<BsonDocument>.Empty)
            .FirstAsync();

        // Nothing is lost by not declaring a key: the document is stored with an _id regardless.
        Assert.True(raw.Contains("_id"));
        Assert.Equal(BsonType.ObjectId, raw["_id"].BsonType);
    }

    [Fact]
    public async Task Insert_many_works_without_a_key()
    {
        await _audit.InsertManyAsync([
            new AuditEntry { Action = "a" }, new AuditEntry { Action = "b" }, new AuditEntry { Action = "c" }
        ]);

        Assert.Equal(3, await _audit.CountAsync());
    }

    [Fact]
    public async Task Collection_name_is_taken_from_the_attribute_when_the_options_are_silent()
    {
        var signals = new MongoRepository<Signal>(fixture.CreateClient().GetDatabase(_database));

        Assert.Equal("signals", signals.CollectionName);

        await signals.InsertOneAsync(new Signal { Name = "temp", Value = 21.5 });

        Assert.Equal(1, await signals.CountAsync());
    }

    [Fact]
    public void A_type_with_neither_attribute_nor_option_fails_with_an_actionable_message()
    {
        var database = fixture.CreateClient().GetDatabase(_database);

        var exception = Assert.Throws<RepositoryException>(
            () => new MongoRepository<AuditEntry>(database));

        Assert.Contains("[BsonCollection", exception.Message, StringComparison.Ordinal);
        Assert.Contains("CollectionName", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Updates_and_deletes_work_by_filter()
    {
        await _audit.InsertManyAsync([
            new AuditEntry { Action = "keep", Severity = 1 },
            new AuditEntry { Action = "raise", Severity = 1 },
            new AuditEntry { Action = "drop", Severity = 9 }
        ]);

        var updated = await _audit.UpdateOneAsync(
            x => x.Action == "raise", Builders<AuditEntry>.Update.Inc(x => x.Severity, 4));

        Assert.Equal(1, updated.ModifiedCount);
        Assert.Equal(1, await _audit.DeleteManyAsync(x => x.Severity > 8));
        Assert.Equal(2, await _audit.CountAsync());

        var raised = await _audit.FindOneAsync(x => x.Action == "raise");

        Assert.NotNull(raised);
        Assert.Equal(5, raised.Severity);
    }

    [Fact]
    public async Task Replace_by_filter_works_without_a_key()
    {
        await _audit.InsertOneAsync(new AuditEntry { Action = "old", Actor = "bruno" });

        var result = await _audit.ReplaceOneAsync(
            x => x.Actor == "bruno",
            new AuditEntry { Action = "new", Actor = "bruno", Severity = 3 });

        Assert.Equal(1, result.ModifiedCount);

        var found = await _audit.FindOneAsync(x => x.Actor == "bruno");

        Assert.NotNull(found);
        Assert.Equal("new", found.Action);
        Assert.Equal(3, found.Severity);
    }

    [Fact]
    public async Task Indexes_work_on_a_keyless_type()
    {
        var indexed = new MongoRepository<AuditEntry>(
            fixture.CreateClient().GetDatabase(_database),
            new MongoRepositoryOptions<AuditEntry>
            {
                CollectionName = "audit_indexed",
                IndexCreation = IndexCreationMode.FirstUse
            }
                .AddIndex(MongoIndexes<AuditEntry>.Ascending(x => x.Actor))
                .AddIndex(MongoIndexes<AuditEntry>.Descending(x => x.At)));

        await indexed.EnsureIndexesAsync();

        var cursor = await indexed.Collection.Indexes.ListAsync();
        var names = (await cursor.ToListAsync()).Select(i => i["name"].AsString).ToList();

        Assert.Contains("Actor_1", names);
        Assert.Contains("At_-1", names);
    }

    [Fact]
    public async Task Transactions_work_on_a_keyless_type()
    {
        await _audit.WithTransactionAsync(async (session, token) =>
        {
            await _audit.InsertOneAsync(new AuditEntry { Action = "tx1" }, session, token);
            await _audit.InsertOneAsync(new AuditEntry { Action = "tx2" }, session, token);
        });

        Assert.Equal(2, await _audit.CountAsync());
    }

    [Fact]
    public async Task A_bson_document_repository_handles_a_collection_with_no_fixed_shape()
    {
        // The most dynamic case there is: no compile-time shape at all.
        var dynamicRepository = new MongoRepository<BsonDocument>(
            fixture.CreateClient().GetDatabase(_database),
            new MongoRepositoryOptions<BsonDocument>().InCollection("events"));

        await dynamicRepository.InsertManyAsync([
            new BsonDocument { { "kind", "click" }, { "x", 10 } },
            new BsonDocument { { "kind", "click" }, { "x", 20 } },
            new BsonDocument { { "kind", "scroll" }, { "depth", 0.8 } }
        ]);

        var clicks = await dynamicRepository.Query()
            .WhereField("kind", "click")
            .OrderBy(x => x["x"])
            .ToListAsync();

        Assert.Equal(2, clicks.Count);
        Assert.Equal(10, clicks[0]["x"].AsInt32);
        Assert.Equal(3, await dynamicRepository.CountAsync());
    }

    [Fact]
    public async Task An_undecorated_type_is_readable_through_the_ignore_extra_elements_option()
    {
        // Stands in for a model from a package you cannot decorate. Without the option, the first read
        // fails with "Element '_id' does not match any field or property of class ExternalModel".
        var external = new MongoRepository<ExternalModel>(
            fixture.CreateClient().GetDatabase(_database),
            new MongoRepositoryOptions<ExternalModel>()
                .InCollection("external")
                .IgnoringExtraElements());

        await external.InsertOneAsync(new ExternalModel { Name = "from-a-package", Count = 7 });

        var found = await external.FindOneAsync(x => x.Name == "from-a-package");

        Assert.NotNull(found);
        Assert.Equal(7, found.Count);
    }

    [Fact]
    public async Task A_keyed_repository_also_resolves_as_keyless()
    {
        // The narrower interface is what a consumer that only filters should depend on.
        IMongoRepository<Order> asKeyless = new MongoRepository<Order, Guid>(
            fixture.CreateClient().GetDatabase(_database));

        await asKeyless.InsertOneAsync(new Order { CustomerEmail = "x@example.com" });

        Assert.Equal(1, await asKeyless.CountAsync());
        Assert.NotNull(await asKeyless.Query().FirstOrDefaultAsync());
    }
}
