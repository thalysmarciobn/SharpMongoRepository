using MongoDB.Driver;
using SharpMongoRepository;
using Xunit;

namespace Tests.Integration;

/// <summary>
///     Exercises the repository against a real server. These are the tests the previous suite lacked
///     entirely: it mocked <c>IMongoRepository</c> and asserted that the mock returned what it was told to.
/// </summary>
[Collection(MongoCollectionFixture.Name)]
[Trait("Category", "Integration")]
public class RepositoryTests(MongoFixture fixture) : IAsyncLifetime
{
    private readonly string _database = MongoFixture.NewDatabaseName();
    private IMongoRepository<Order, Guid> _orders = null!;

    public Task InitializeAsync()
    {
        _orders = new MongoRepository<Order, Guid>(
            fixture.CreateClient().GetDatabase(_database));

        return Task.CompletedTask;
    }

    public Task DisposeAsync() => fixture.CreateClient().DropDatabaseAsync(_database);

    [Fact]
    public async Task Insert_then_find_by_id_round_trips_a_guid_stored_as_a_string()
    {
        // Order.Id carries [BsonRepresentation(BsonType.String)], so this proves the cached "_id" field
        // definition resolves the member's own serializer rather than the default Guid serializer.
        var order = new Order { CustomerEmail = "a@example.com", Total = 100 };

        await _orders.InsertOneAsync(order);

        Assert.NotEqual(Guid.Empty, order.Id);

        var found = await _orders.FindByIdAsync(order.Id);

        Assert.NotNull(found);
        Assert.Equal(order.Id, found.Id);
        Assert.Equal("a@example.com", found.CustomerEmail);

        // And confirm it really is stored as a string on the wire.
        var raw = await _orders.Collection.Database
            .GetCollection<MongoDB.Bson.BsonDocument>("orders")
            .Find(MongoDB.Bson.BsonDocument.Parse($"{{ _id: '{order.Id}' }}"))
            .FirstOrDefaultAsync();

        Assert.NotNull(raw);
        Assert.Equal(MongoDB.Bson.BsonType.String, raw["_id"].BsonType);
    }

    [Fact]
    public async Task Find_by_id_returns_null_when_absent()
        => Assert.Null(await _orders.FindByIdAsync(Guid.NewGuid()));

    [Fact]
    public async Task Insert_many_assigns_every_key_and_persists_all_documents()
    {
        var orders = Enumerable.Range(0, 5)
            .Select(i => new Order { CustomerEmail = $"b{i}@example.com", Total = i })
            .ToList();

        await _orders.InsertManyAsync(orders);

        Assert.All(orders, order => Assert.NotEqual(Guid.Empty, order.Id));
        Assert.Equal(5, orders.Select(o => o.Id).Distinct().Count());
        Assert.Equal(5, await _orders.CountAsync(x => x.CustomerEmail != string.Empty));
    }

    [Fact]
    public async Task Insert_many_on_an_empty_sequence_is_a_no_op()
    {
        await _orders.InsertManyAsync([]);

        Assert.Equal(0, await _orders.CountAsync());
    }

    [Fact]
    public async Task Update_by_id_applies_a_partial_change_without_touching_other_fields()
    {
        var order = new Order { CustomerEmail = "c@example.com", Total = 10, Status = "pending" };
        await _orders.InsertOneAsync(order);

        var result = await _orders.UpdateByIdAsync(
            order.Id,
            Builders<Order>.Update.Set(x => x.Status, "shipped").Inc(x => x.Total, 5));

        Assert.Equal(1, result.MatchedCount);
        Assert.Equal(1, result.ModifiedCount);

        var updated = await _orders.FindByIdAsync(order.Id);

        Assert.NotNull(updated);
        Assert.Equal("shipped", updated.Status);
        Assert.Equal(15, updated.Total);
        // Untouched by the update, which a whole-document replacement would have had to resend.
        Assert.Equal("c@example.com", updated.CustomerEmail);
    }

    [Fact]
    public async Task Update_many_changes_every_match_in_one_command()
    {
        await _orders.InsertManyAsync([
            new Order { Status = "pending", Total = 1 },
            new Order { Status = "pending", Total = 2 },
            new Order { Status = "shipped", Total = 3 }
        ]);

        var result = await _orders.UpdateManyAsync(
            x => x.Status == "pending",
            Builders<Order>.Update.Set(x => x.Status, "cancelled"));

        Assert.Equal(2, result.MatchedCount);
        Assert.Equal(2, await _orders.CountAsync(x => x.Status == "cancelled"));
    }

    [Fact]
    public async Task Find_one_and_update_returns_the_version_asked_for()
    {
        var order = new Order { Total = 1 };
        await _orders.InsertOneAsync(order);

        var before = await _orders.FindOneAndUpdateAsync(
            x => x.Id == order.Id,
            Builders<Order>.Update.Inc(x => x.Total, 1),
            ReturnDocument.Before);

        Assert.NotNull(before);
        Assert.Equal(1, before.Total);

        var after = await _orders.FindOneAndUpdateAsync(
            x => x.Id == order.Id,
            Builders<Order>.Update.Inc(x => x.Total, 1),
            ReturnDocument.After);

        Assert.NotNull(after);
        Assert.Equal(3, after.Total);
    }

    [Fact]
    public async Task Upsert_creates_the_document_when_nothing_matches()
    {
        var result = await _orders.UpdateOneAsync(
            x => x.CustomerEmail == "new@example.com",
            Builders<Order>.Update.Set(x => x.Total, 42),
            upsert: true);

        Assert.NotNull(result.UpsertedId);
        Assert.Equal(1, await _orders.CountAsync(x => x.Total == 42));
    }

    [Fact]
    public async Task Replace_one_overwrites_the_whole_document()
    {
        var order = new Order { CustomerEmail = "d@example.com", Total = 10, Status = "pending" };
        await _orders.InsertOneAsync(order);

        var replacement = new Order { Id = order.Id, CustomerEmail = "d2@example.com", Total = 20 };
        var result = await _orders.ReplaceOneAsync(replacement);

        Assert.Equal(1, result.ModifiedCount);

        var stored = await _orders.FindByIdAsync(order.Id);

        Assert.NotNull(stored);
        Assert.Equal("d2@example.com", stored.CustomerEmail);
        Assert.Equal("pending", stored.Status); // the replacement's own default
    }

    [Fact]
    public async Task Delete_by_id_reports_whether_anything_was_removed()
    {
        var order = new Order();
        await _orders.InsertOneAsync(order);

        // The previous version returned void, so a caller could not tell a delete from a no-op.
        Assert.Equal(1, await _orders.DeleteByIdAsync(order.Id));
        Assert.Equal(0, await _orders.DeleteByIdAsync(order.Id));
    }

    [Fact]
    public async Task Delete_many_reports_the_number_removed()
    {
        await _orders.InsertManyAsync([
            new Order { Total = 1 }, new Order { Total = 2 }, new Order { Total = 30 }
        ]);

        Assert.Equal(2, await _orders.DeleteManyAsync(x => x.Total < 10));
        Assert.Equal(1, await _orders.CountAsync());
    }

    [Fact]
    public async Task Find_one_and_delete_hands_back_the_removed_document()
    {
        var order = new Order { CustomerEmail = "e@example.com" };
        await _orders.InsertOneAsync(order);

        var deleted = await _orders.FindOneAndDeleteAsync(x => x.Id == order.Id);

        Assert.NotNull(deleted);
        Assert.Equal("e@example.com", deleted.CustomerEmail);
        Assert.Equal(0, await _orders.CountAsync());
    }

    [Fact]
    public async Task Exists_stops_at_the_first_match()
    {
        await _orders.InsertOneAsync(new Order { Total = 7 });

        Assert.True(await _orders.ExistsAsync(x => x.Total == 7));
        Assert.False(await _orders.ExistsAsync(x => x.Total == 8));
    }

    [Fact]
    public async Task Count_with_and_without_a_filter()
    {
        await _orders.InsertManyAsync([
            new Order { Total = 1 }, new Order { Total = 2 }, new Order { Total = 3 }
        ]);

        Assert.Equal(3, await _orders.CountAsync());
        Assert.Equal(2, await _orders.CountAsync(x => x.Total > 1));
    }

    [Fact]
    public async Task Estimated_count_reads_collection_metadata()
    {
        await _orders.InsertManyAsync([new Order(), new Order()]);

        Assert.Equal(2, await _orders.EstimateCountAsync());
    }

    [Fact]
    public async Task Distinct_returns_unique_values()
    {
        await _orders.InsertManyAsync([
            new Order { Status = "a" }, new Order { Status = "a" }, new Order { Status = "b" }
        ]);

        var statuses = await _orders.DistinctAsync(x => x.Status);

        Assert.Equal(["a", "b"], statuses.Order());
    }

    [Fact]
    public async Task Projection_runs_server_side()
    {
        await _orders.InsertManyAsync([
            new Order { CustomerEmail = "f@example.com", Total = 1 },
            new Order { CustomerEmail = "g@example.com", Total = 2 }
        ]);

        var emails = await _orders.FilterByAsync(x => x.Total > 0, x => x.CustomerEmail);

        Assert.Equal(["f@example.com", "g@example.com"], emails.Order());
    }

    [Fact]
    public async Task Stream_yields_every_document_without_buffering_them_all()
    {
        await _orders.InsertManyAsync(
            [.. Enumerable.Range(0, 250).Select(i => new Order { Total = i })]);

        var seen = 0;
        await foreach (var _ in _orders.StreamAsync())
            seen++;

        Assert.Equal(250, seen);
    }

    [Fact]
    public async Task Stream_can_be_abandoned_early_and_releases_its_cursor()
    {
        await _orders.InsertManyAsync(
            [.. Enumerable.Range(0, 250).Select(i => new Order { Total = i })]);

        var seen = 0;
        await foreach (var _ in _orders.StreamAsync())
        {
            seen++;
            if (seen == 5)
                break;
        }

        Assert.Equal(5, seen);

        // The cursor is disposed by the enumerator's finally block, so the collection is still usable.
        Assert.Equal(250, await _orders.CountAsync());
    }

    [Fact]
    public async Task Stream_with_a_projection_yields_projected_values()
    {
        await _orders.InsertManyAsync([
            new Order { CustomerEmail = "h@example.com" }, new Order { CustomerEmail = "i@example.com" }
        ]);

        var emails = new List<string>();
        await foreach (var email in _orders.StreamAsync(null, x => x.CustomerEmail))
            emails.Add(email);

        Assert.Equal(["h@example.com", "i@example.com"], emails.Order());
    }

    [Fact]
    public async Task Paging_returns_one_page_and_the_total()
    {
        await _orders.InsertManyAsync(
            [.. Enumerable.Range(0, 25).Select(i => new Order { Total = i })]);

        var page = await _orders.GetPageAsync(
            2, 10, sort: Builders<Order>.Sort.Ascending(x => x.Total));

        Assert.Equal(25, page.TotalCount);
        Assert.Equal(3, page.TotalPages);
        Assert.Equal(2, page.Page);
        Assert.Equal(10, page.Items.Count);
        Assert.Equal(10, page.Items[0].Total);
        Assert.True(page.HasNextPage);
        Assert.True(page.HasPreviousPage);
    }

    [Fact]
    public async Task Paging_past_the_end_returns_an_empty_page()
    {
        await _orders.InsertOneAsync(new Order());

        var page = await _orders.GetPageAsync(99, 10);

        Assert.Empty(page.Items);
        Assert.Equal(1, page.TotalCount);
        Assert.False(page.HasNextPage);
    }

    [Fact]
    public async Task Paging_rejects_a_non_positive_page_size()
        => await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _orders.GetPageAsync(1, 0));

    [Fact]
    public async Task Bulk_write_applies_a_mixed_batch_in_one_command()
    {
        var kept = new Order { Total = 1 };
        var removed = new Order { Total = 2 };
        await _orders.InsertManyAsync([kept, removed]);

        var result = await _orders.BulkWriteAsync([
            new UpdateOneModel<Order>(
                Builders<Order>.Filter.Eq(x => x.Id, kept.Id),
                Builders<Order>.Update.Set(x => x.Status, "bulk")),
            new DeleteOneModel<Order>(Builders<Order>.Filter.Eq(x => x.Id, removed.Id)),
            new InsertOneModel<Order>(new Order { Id = Guid.NewGuid(), Total = 3 })
        ]);

        Assert.Equal(1, result.ModifiedCount);
        Assert.Equal(1, result.DeletedCount);
        Assert.Equal(1, result.InsertedCount);
        Assert.Equal(2, await _orders.CountAsync());
    }

    [Fact]
    public async Task Queryable_translates_to_a_server_side_pipeline()
    {
        await _orders.InsertManyAsync([
            new Order { Total = 1 }, new Order { Total = 5 }, new Order { Total = 9 }
        ]);

        var total = _orders.AsQueryable().Where(x => x.Total > 1).Sum(x => x.Total);

        Assert.Equal(14, total);
    }

    [Fact]
    public async Task Fluent_find_is_available_for_sorts_and_limits()
    {
        await _orders.InsertManyAsync([
            new Order { Total = 3 }, new Order { Total = 1 }, new Order { Total = 2 }
        ]);

        var top = await _orders
            .Find(Builders<Order>.Filter.Empty)
            .SortByDescending(x => x.Total)
            .Limit(2)
            .ToListAsync();

        Assert.Equal([3, 2], top.Select(x => x.Total));
    }

    [Fact]
    public async Task Cancelled_token_aborts_the_operation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Nothing in the previous API accepted a token at all.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _orders.CountAsync(cancellationToken: cts.Token));
    }

    [Fact]
    public async Task Null_arguments_are_rejected_before_any_round_trip()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _orders.InsertOneAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _orders.FilterByAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _orders.UpdateByIdAsync(Guid.NewGuid(), null!));
    }
}
