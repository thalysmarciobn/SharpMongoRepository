using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using SharpMongoRepository;
using SharpMongoRepository.Extensions;
using SharpMongoRepository.Indexing;
using Xunit;

#pragma warning disable CS0618 // The point of these tests is to exercise the obsolete surface.

namespace Tests.Unit;

/// <summary>
///     The 0.0.x entry points are kept as obsolete forwarders until 0.2.0, so they need to keep working
///     rather than merely keep compiling.
/// </summary>
public class ObsoleteForwarderTests
{
    [Fact]
    public void CreateAscendingIndex_forwards_and_now_produces_a_correct_name()
    {
        var index = MongoDocument<Order, Guid>.CreateAscendingIndex(x => x.CustomerEmail);

        Assert.Equal("customer_email_1", index.Options.Name);
        Assert.Equal(new BsonDocument("customer_email", 1), index.RenderKeys());
    }

    [Fact]
    public void CreateDescendingIndex_forwards()
    {
        var index = MongoDocument<Order, Guid>.CreateDescendingIndex(x => x.Total, unique: true);

        Assert.Equal("Total_-1", index.Options.Name);
        Assert.True(index.Options.Unique);
    }

    [Fact]
    public void CreateCompoundIndex_forwards_through_the_object_typed_field_helper()
    {
        // The old Field signature took Expression<Func<TDocument, object>>, so a value-type member arrives
        // wrapped in a Convert node. This asserts the driver unwraps it rather than indexing something odd.
        var index = MongoDocument<Order, Guid>.CreateCompoundIndex(
            false,
            MongoDocument<Order, Guid>.Field(IndexDirection.Ascending, x => x.Status),
            MongoDocument<Order, Guid>.Field(IndexDirection.Descending, x => x.Total));

        var expected = new BsonDocument { { "Status", 1 }, { "Total", -1 } };

        Assert.Equal(expected, index.RenderKeys());
        Assert.Equal("Status_1_Total_-1", index.Options.Name);
    }

    [Fact]
    public void Field_helper_unwraps_a_boxed_value_type_member()
    {
        var field = MongoDocument<Order, Guid>.Field(IndexDirection.Ascending, x => x.CreatedAt);
        var index = MongoIndexes<Order>.Compound(field);

        Assert.Equal(new BsonDocument("CreatedAt", 1), index.RenderKeys());
    }

    [Fact]
    public void Field_helper_unwraps_a_boxed_nested_member()
    {
        var field = MongoDocument<Order, Guid>.Field(IndexDirection.Ascending, x => x.Address!.City);
        var index = MongoIndexes<Order>.Compound(field);

        Assert.Equal(new BsonDocument("Address.city", 1), index.RenderKeys());
    }

    [Fact]
    public void Obsolete_registration_overload_still_registers_its_indexes()
    {
        var services = new ServiceCollection();

        services.AddMongoRepository<Order, Guid>(
            "mongodb://localhost:27017",
            "test",
            [MongoIndexes<Order>.Ascending(x => x.Total)],
            options => options.OperationTimeout = TimeSpan.FromSeconds(3));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var repository = scope.ServiceProvider.GetRequiredService<IMongoRepository<Order, Guid>>();

        Assert.Equal("orders", repository.Collection.CollectionNamespace.CollectionName);
    }

    [Fact]
    public void Obsolete_registration_overload_tolerates_a_null_index_list()
    {
        var services = new ServiceCollection();

        // A bare `null` here is ambiguous between the obsolete index-list overload and the current
        // configure-delegate one, so the type has to be stated. Documented in CHANGELOG.md.
        services.AddMongoRepository<Order, Guid>(
            "mongodb://localhost:27017", "test", (List<MongoIndex<Order>>?)null);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IMongoRepository<Order, Guid>>());
    }
}
