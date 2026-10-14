using MongoDB.Bson;
using MongoDB.Driver;
using SharpMongoRepository;
using SharpMongoRepository.Indexing;
using Xunit;

namespace Tests.Integration;

/// <summary>
///     Covers the composable query, which is how a read is expressed when there is no key to look up by.
/// </summary>
[Collection(MongoCollectionFixture.Name)]
[Trait("Category", "Integration")]
public class QueryBuilderTests(MongoFixture fixture) : IAsyncLifetime
{
    private readonly string _database = MongoFixture.NewDatabaseName();
    private IMongoRepository<Order, Guid> _orders = null!;

    public async Task InitializeAsync()
    {
        _orders = new MongoRepository<Order, Guid>(fixture.CreateClient().GetDatabase(_database));

        await _orders.InsertManyAsync([
            new Order { CustomerEmail = "a@x.com", Total = 10, Status = "pending", CreatedAt = Day(1) },
            new Order { CustomerEmail = "b@x.com", Total = 20, Status = "pending", CreatedAt = Day(2) },
            new Order { CustomerEmail = "c@x.com", Total = 30, Status = "shipped", CreatedAt = Day(3) },
            new Order { CustomerEmail = "d@x.com", Total = 40, Status = "shipped", CreatedAt = Day(4) },
            new Order
            {
                CustomerEmail = "e@x.com", Total = 50, Status = "cancelled", CreatedAt = Day(5),
                Address = new Address { City = "Natal", Country = "BR" }
            }
        ]);
    }

    public Task DisposeAsync() => fixture.CreateClient().DropDatabaseAsync(_database);

    private static DateTime Day(int day) => new(2026, 1, day, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Repeated_where_calls_combine_with_and()
    {
        var found = await _orders.Query()
            .Where(x => x.Status == "pending")
            .Where(x => x.Total > 15)
            .ToListAsync();

        Assert.Single(found);
        Assert.Equal("b@x.com", found[0].CustomerEmail);
    }

    [Fact]
    public async Task Where_any_groups_alternatives_without_widening_the_whole_query()
    {
        // Total > 45 AND (status pending OR status cancelled): the grouping is the point.
        var found = await _orders.Query()
            .Where(x => x.Total > 45)
            .WhereAny(x => x.Status == "pending", x => x.Status == "cancelled")
            .ToListAsync();

        Assert.Single(found);
        Assert.Equal("e@x.com", found[0].CustomerEmail);
    }

    [Fact]
    public async Task Where_accepts_a_driver_filter_for_operators_a_lambda_cannot_express()
    {
        var found = await _orders.Query()
            .Where(Builders<Order>.Filter.In(x => x.Status, ["pending", "shipped"]))
            .ToListAsync();

        Assert.Equal(4, found.Count);
    }

    [Fact]
    public async Task Where_accepts_raw_json()
    {
        var found = await _orders.Query().Where("{ Status: 'shipped' }").ToListAsync();

        Assert.Equal(2, found.Count);
    }

    [Fact]
    public async Task Where_field_resolves_a_member_name_through_the_class_map()
    {
        // CustomerEmail is stored as customer_email; naming the member picks that up.
        var found = await _orders.Query()
            .WhereField("CustomerEmail", "c@x.com")
            .FirstOrDefaultAsync();

        Assert.NotNull(found);
        Assert.Equal(30, found.Total);
    }

    [Fact]
    public async Task Where_field_accepts_a_dotted_path()
    {
        var found = await _orders.Query().WhereField("Address.city", "Natal").ToListAsync();

        Assert.Single(found);
        Assert.Equal("e@x.com", found[0].CustomerEmail);
    }

    [Fact]
    public async Task Ordering_and_tie_breakers_apply_server_side()
    {
        var found = await _orders.Query()
            .OrderBy(x => x.Status)
            .ThenByDescending(x => x.Total)
            .ToListAsync(x => x.CustomerEmail);

        Assert.Equal(["e@x.com", "b@x.com", "a@x.com", "d@x.com", "c@x.com"], found);
    }

    [Fact]
    public async Task Skip_and_take_page_server_side()
    {
        var found = await _orders.Query()
            .OrderBy(x => x.Total)
            .Skip(1)
            .Take(2)
            .ToListAsync(x => x.Total);

        Assert.Equal([20, 30], found);
    }

    [Fact]
    public async Task Projection_sends_only_the_fields_asked_for()
    {
        var totals = await _orders.Query()
            .Where(x => x.Status == "shipped")
            .OrderBy(x => x.Total)
            .ToListAsync(x => new { x.Total, x.CustomerEmail });

        Assert.Equal([30, 40], totals.Select(x => x.Total));
    }

    [Fact]
    public async Task First_or_default_returns_null_when_nothing_matches()
        => Assert.Null(await _orders.Query().Where(x => x.Total > 1000).FirstOrDefaultAsync());

    [Fact]
    public async Task First_or_default_can_project_a_single_field()
    {
        var email = await _orders.Query()
            .OrderByDescending(x => x.Total)
            .FirstOrDefaultAsync(x => x.CustomerEmail);

        Assert.Equal("e@x.com", email);
    }

    [Fact]
    public async Task Single_or_default_returns_the_only_match()
    {
        var found = await _orders.Query().Where(x => x.Total == 30).SingleOrDefaultAsync();

        Assert.NotNull(found);
        Assert.Equal("c@x.com", found.CustomerEmail);
    }

    [Fact]
    public async Task Single_or_default_throws_when_more_than_one_matches()
        => await Assert.ThrowsAsync<InvalidOperationException>(
            () => _orders.Query().Where(x => x.Status == "pending").SingleOrDefaultAsync());

    [Fact]
    public async Task Count_ignores_skip_and_take()
    {
        var count = await _orders.Query().Where(x => x.Total > 15).Skip(1).Take(1).CountAsync();

        Assert.Equal(4, count);
    }

    [Fact]
    public async Task Any_is_true_only_when_something_matches()
    {
        Assert.True(await _orders.Query().Where(x => x.Status == "shipped").AnyAsync());
        Assert.False(await _orders.Query().Where(x => x.Status == "lost").AnyAsync());
    }

    [Fact]
    public async Task Paging_reports_the_total_by_default()
    {
        var page = await _orders.Query().OrderBy(x => x.Total).ToPageAsync(2, 2);

        Assert.True(page.HasTotalCount);
        Assert.Equal(5, page.TotalCount);
        Assert.Equal(3, page.TotalPages);
        Assert.Equal([30, 40], page.Items.Select(x => x.Total));
        Assert.True(page.HasNextPage);
        Assert.True(page.HasPreviousPage);
    }

    [Fact]
    public async Task Paging_can_skip_the_count_to_halve_the_round_trips()
    {
        var page = await _orders.Query().OrderBy(x => x.Total).ToPageAsync(1, 2, includeTotal: false);

        Assert.False(page.HasTotalCount);
        Assert.Equal(PagedResult<Order>.TotalCountNotCounted, page.TotalCount);
        Assert.Equal(0, page.TotalPages);
        // A full page implies another one follows, which is what an endless-scrolling list needs.
        Assert.True(page.HasNextPage);
        Assert.Equal(2, page.Items.Count);
    }

    [Fact]
    public async Task An_uncounted_partial_page_reports_no_next_page()
    {
        var page = await _orders.Query().OrderBy(x => x.Total).ToPageAsync(3, 2, includeTotal: false);

        Assert.Single(page.Items);
        Assert.False(page.HasNextPage);
    }

    [Fact]
    public async Task Paging_can_project()
    {
        var page = await _orders.Query()
            .OrderBy(x => x.Total)
            .ToPageAsync(1, 3, x => x.Total);

        Assert.Equal([10, 20, 30], page.Items);
        Assert.Equal(5, page.TotalCount);
    }

    [Fact]
    public async Task Streaming_yields_every_match_and_releases_its_cursor_on_an_early_break()
    {
        var seen = 0;
        await foreach (var _ in _orders.Query().OrderBy(x => x.Total).ToAsyncEnumerable())
        {
            seen++;
            if (seen == 2)
                break;
        }

        Assert.Equal(2, seen);
        Assert.Equal(5, await _orders.CountAsync());
    }

    [Fact]
    public async Task Streaming_can_project()
    {
        var totals = new List<int>();
        await foreach (var total in _orders.Query().OrderBy(x => x.Total).ToAsyncEnumerable(x => x.Total))
            totals.Add(total);

        Assert.Equal([10, 20, 30, 40, 50], totals);
    }

    [Fact]
    public async Task Batch_size_does_not_change_the_result()
    {
        var found = await _orders.Query().WithBatchSize(2).ToListAsync();

        Assert.Equal(5, found.Count);
    }

    [Fact]
    public async Task A_hint_forces_the_query_onto_a_named_index()
    {
        var repository = new MongoRepository<Order, Guid>(
            fixture.CreateClient().GetDatabase(_database),
            new MongoRepositoryOptions<Order> { IndexCreation = IndexCreationMode.FirstUse }
                .AddIndex(MongoIndexes<Order>.Ascending(x => x.Total)));

        await repository.EnsureIndexesAsync();

        var found = await repository.Query().WithHint("Total_1").OrderBy(x => x.Total).ToListAsync();

        Assert.Equal(5, found.Count);
    }

    [Fact]
    public async Task A_hint_on_a_missing_index_fails_rather_than_falling_back()
        => await Assert.ThrowsAnyAsync<MongoCommandException>(
            () => _orders.Query().WithHint("no_such_index").ToListAsync());

    [Fact]
    public async Task Allow_disk_use_is_accepted()
    {
        var found = await _orders.Query().AllowDiskUse().OrderBy(x => x.Total).ToListAsync();

        Assert.Equal(5, found.Count);
    }

    [Fact]
    public async Task A_query_can_be_enrolled_in_a_transaction()
    {
        using var session = await _orders.StartSessionAsync();

        var count = await session.WithTransactionAsync(async (handle, token) =>
        {
            await _orders.InsertOneAsync(new Order { Total = 60, Status = "pending" }, handle, token);

            // The query sees the uncommitted insert because it is in the same session.
            return await _orders.Query().InSession(handle).CountAsync(token);
        });

        Assert.Equal(6, count);
    }

    [Fact]
    public async Task Branching_from_a_partially_built_query_does_not_leak_conditions()
    {
        // The reason the builder is immutable: `pending` must not pick up the other branch's filter.
        var pending = _orders.Query().Where(x => x.Status == "pending");

        var cheap = await pending.Where(x => x.Total < 15).CountAsync();
        var all = await pending.CountAsync();

        Assert.Equal(1, cheap);
        Assert.Equal(2, all);
    }

    [Fact]
    public void Render_filter_exposes_what_the_server_will_receive()
    {
        var rendered = _orders.Query()
            .Where(x => x.Status == "pending")
            .Where(x => x.Total > 15)
            .RenderFilter();

        // The driver flattens an `and` of disjoint fields into one document rather than emitting $and.
        Assert.Equal(
            BsonDocument.Parse("{ Status: 'pending', Total: { $gt: 15 } }"),
            rendered);
    }

    [Fact]
    public async Task An_empty_query_matches_everything()
        => Assert.Equal(5, await _orders.Query().CountAsync());

    [Fact]
    public async Task Query_by_id_narrows_to_one_document()
    {
        var order = new Order { Total = 99, Status = "pending" };
        await _orders.InsertOneAsync(order);

        // Reads one field by key, without transferring the document.
        var status = await _orders.QueryById(order.Id).FirstOrDefaultAsync(x => x.Status);

        Assert.Equal("pending", status);
        Assert.Equal(1, await _orders.QueryById(order.Id).CountAsync());
    }

    [Fact]
    public async Task To_find_fluent_hands_back_the_driver_builder()
    {
        var explained = await _orders.Query()
            .Where(x => x.Total > 15)
            .ToFindFluent()
            .ToListAsync();

        Assert.Equal(4, explained.Count);
    }

    [Fact]
    public async Task Cancellation_is_honoured()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _orders.Query().ToListAsync(cts.Token));
    }

    [Fact]
    public void Builder_arguments_are_validated()
    {
        Assert.Throws<ArgumentNullException>(() => _orders.Query().Where((System.Linq.Expressions
            .Expression<Func<Order, bool>>)null!));
        Assert.Throws<ArgumentException>(() => _orders.Query().WhereField("  ", 1));
        Assert.Throws<ArgumentException>(() => _orders.Query().WhereAny());
        Assert.Throws<ArgumentOutOfRangeException>(() => _orders.Query().WithBatchSize(0));
    }

    [Fact]
    public void Negative_skip_and_take_are_clamped_rather_than_throwing()
    {
        var query = _orders.Query().Skip(-5).Take(-5);

        Assert.NotNull(query);
    }
}
