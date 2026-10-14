using MongoDB.Bson;
using SharpMongoRepository;
using SharpMongoRepository.Indexing;
using Xunit;

namespace Tests.Unit;

/// <summary>
///     Covers index key rendering and naming. Every test here fails against the 0.0.x implementation.
/// </summary>
public class IndexDefinitionTests
{
    [Fact]
    public void Ascending_and_descending_on_the_same_field_get_distinct_names()
    {
        var ascending = MongoIndexes<Order, Guid>.Ascending(x => x.Total);
        var descending = MongoIndexes<Order, Guid>.Descending(x => x.Total);

        // Both used to be named "Total", so the ensurer deduplicated them by name and silently created
        // only the first.
        Assert.Equal("Total_1", ascending.Options.Name);
        Assert.Equal("Total_-1", descending.Options.Name);
        Assert.NotEqual(ascending.Options.Name, descending.Options.Name);
    }

    [Fact]
    public void Nested_field_selector_indexes_the_full_path()
    {
        var index = MongoIndexes<Order, Guid>.Ascending(x => x.Address!.City);

        // Previously rendered { "City": 1 }: an index on a root-level field no document has.
        Assert.Equal(new BsonDocument("Address.city", 1), index.RenderKeys());
        Assert.Equal("Address.city_1", index.Options.Name);
    }

    [Fact]
    public void BsonElement_rename_is_honoured_in_keys_and_name()
    {
        var index = MongoIndexes<Order, Guid>.Ascending(x => x.CustomerEmail);

        Assert.Equal(new BsonDocument("customer_email", 1), index.RenderKeys());
        Assert.Equal("customer_email_1", index.Options.Name);
    }

    [Fact]
    public void Compound_index_preserves_field_order_and_directions()
    {
        var index = MongoIndexes<Order, Guid>.Compound(
            MongoIndexes<Order, Guid>.Field(IndexDirection.Ascending, x => x.CustomerEmail),
            MongoIndexes<Order, Guid>.Field(IndexDirection.Descending, x => x.CreatedAt));

        var expected = new BsonDocument { { "customer_email", 1 }, { "CreatedAt", -1 } };

        Assert.Equal(expected, index.RenderKeys());
        Assert.Equal("customer_email_1_CreatedAt_-1", index.Options.Name);
    }

    [Fact]
    public void Compound_index_with_reversed_directions_is_a_different_index()
    {
        var a = MongoIndexes<Order, Guid>.Compound(
            MongoIndexes<Order, Guid>.Field(IndexDirection.Ascending, x => x.Status),
            MongoIndexes<Order, Guid>.Field(IndexDirection.Descending, x => x.Total));

        var b = MongoIndexes<Order, Guid>.Compound(
            MongoIndexes<Order, Guid>.Field(IndexDirection.Descending, x => x.Status),
            MongoIndexes<Order, Guid>.Field(IndexDirection.Ascending, x => x.Total));

        // Both used to be named "Status_Total".
        Assert.NotEqual(a.Options.Name, b.Options.Name);
    }

    [Fact]
    public void Unique_flag_reaches_the_options()
    {
        Assert.True(MongoIndexes<Order, Guid>.Ascending(x => x.CustomerEmail, unique: true).Options.Unique);
        Assert.False(MongoIndexes<Order, Guid>.Ascending(x => x.CustomerEmail).Options.Unique);
    }

    [Fact]
    public void TimeToLive_index_sets_expiry_and_is_not_unique()
    {
        var index = MongoIndexes<Order, Guid>.TimeToLive(x => x.CreatedAt, TimeSpan.FromDays(30));

        Assert.Equal(TimeSpan.FromDays(30), index.Options.ExpireAfter);
        Assert.False(index.Options.Unique);
        Assert.Equal("CreatedAt_1", index.Options.Name);
    }

    [Fact]
    public void Text_index_name_uses_the_text_marker()
    {
        var index = MongoIndexes<Order, Guid>.Text(x => x.Status);

        Assert.Equal("Status_text", index.Options.Name);
    }

    [Fact]
    public void Hashed_index_name_uses_the_hashed_marker()
    {
        var index = MongoIndexes<Order, Guid>.Hashed(x => x.Status);

        Assert.Equal("Status_hashed", index.Options.Name);
    }

    [Fact]
    public void Configure_hook_can_override_the_generated_name()
    {
        var index = MongoIndexes<Order, Guid>.Ascending(
            x => x.Total,
            configure: options => options.Name = "custom_name");

        Assert.Equal("custom_name", index.Options.Name);
    }

    [Fact]
    public void Configure_hook_can_set_a_sparse_index()
    {
        var index = MongoIndexes<Order, Guid>.Ascending(x => x.Total, configure: o => o.Sparse = true);

        Assert.True(index.Options.Sparse);
    }

    [Fact]
    public void Compound_index_without_fields_is_rejected()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => MongoIndexes<Order, Guid>.Compound(unique: false));

        Assert.Equal("fields", exception.ParamName);
    }

    [Fact]
    public void Null_field_selector_is_rejected()
        => Assert.Throws<ArgumentNullException>(
            () => MongoIndexes<Order, Guid>.Ascending<int>(null!));

    [Fact]
    public void Options_collect_indexes_through_the_fluent_helpers()
    {
        var options = new MongoRepositoryOptions<Order, Guid>()
            .AddIndex(MongoIndexes<Order, Guid>.Ascending(x => x.Total))
            .AddIndexes(
                MongoIndexes<Order, Guid>.Descending(x => x.CreatedAt),
                MongoIndexes<Order, Guid>.Ascending(x => x.Status));

        Assert.Equal(3, options.Indexes.Count);
        Assert.Equal(
            ["Total_1", "CreatedAt_-1", "Status_1"],
            options.Indexes.Select(i => i.Options.Name));
    }
}
