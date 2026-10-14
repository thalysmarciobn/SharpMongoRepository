using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.IdGenerators;
using SharpMongoRepository.Internal;
using Xunit;

namespace Tests.Unit;

/// <summary>
///     Covers identifier generation, including the batch path that used to be skipped entirely.
/// </summary>
public class IdAssignerTests
{
    [Fact]
    public void Guid_key_is_generated_when_empty()
    {
        var order = new Order();

        IdAssigner<Order, Guid>.Assign(order, null);

        Assert.NotEqual(Guid.Empty, order.Id);
    }

    [Fact]
    public void Existing_guid_key_is_left_alone()
    {
        var id = Guid.NewGuid();
        var order = new Order { Id = id };

        IdAssigner<Order, Guid>.Assign(order, null);

        Assert.Equal(id, order.Id);
    }

    [Fact]
    public void ObjectId_key_is_generated_when_empty()
    {
        var @event = new Event();

        IdAssigner<Event, ObjectId>.Assign(@event, null);

        Assert.NotEqual(ObjectId.Empty, @event.Id);
    }

    [Fact]
    public void String_key_is_generated_when_empty()
    {
        // A non-nullable string key is normally initialised to string.Empty, so emptiness has to mean
        // null-or-empty rather than just null.
        var ticket = new Ticket { Id = string.Empty };

        IdAssigner<Ticket, string>.Assign(ticket, null);

        Assert.NotEmpty(ticket.Id);
    }

    [Fact]
    public void Batch_insert_generates_a_distinct_string_key_per_document()
    {
        // The regression this exists for: identifiers were only generated on the single-document path, so
        // InsertMany wrote _id: null for every string-keyed document and failed on the second one.
        var tickets = new List<Ticket>
        {
            new() { Subject = "first" },
            new() { Subject = "second" },
            new() { Subject = "third" }
        };

        IdAssigner<Ticket, string>.Assign(tickets, null);

        Assert.All(tickets, ticket => Assert.NotEmpty(ticket.Id));
        Assert.Equal(3, tickets.Select(t => t.Id).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Batch_insert_generates_a_distinct_guid_key_per_document()
    {
        var orders = new List<Order> { new(), new(), new() };

        IdAssigner<Order, Guid>.Assign(orders, null);

        Assert.All(orders, order => Assert.NotEqual(Guid.Empty, order.Id));
        Assert.Equal(3, orders.Select(o => o.Id).Distinct().Count());
    }

    [Fact]
    public void Batch_insert_preserves_keys_that_are_already_set()
    {
        var kept = Guid.NewGuid();
        var orders = new List<Order> { new() { Id = kept }, new() };

        IdAssigner<Order, Guid>.Assign(orders, null);

        Assert.Equal(kept, orders[0].Id);
        Assert.NotEqual(Guid.Empty, orders[1].Id);
    }

    [Fact]
    public void Batch_insert_works_over_a_lazy_sequence()
    {
        var orders = Enumerable.Range(0, 3).Select(_ => new Order()).ToArray();

        IdAssigner<Order, Guid>.Assign(orders.Where(_ => true), null);

        Assert.All(orders, order => Assert.NotEqual(Guid.Empty, order.Id));
    }

    [Fact]
    public void Int_key_is_left_to_the_caller_rather_than_throwing()
    {
        var counter = new Counter { Id = 7 };

        // No generator exists for int, and that is not an error: it simply means the caller owns the key.
        IdAssigner<Counter, int>.Assign(counter, null);

        Assert.Equal(7, counter.Id);
    }

    [Fact]
    public void Int_key_left_at_its_default_is_not_rewritten()
    {
        var counter = new Counter();

        IdAssigner<Counter, int>.Assign(counter, null);

        Assert.Equal(0, counter.Id);
    }

    [Fact]
    public void Caller_supplied_generator_is_preferred()
    {
        var order = new Order();

        IdAssigner<Order, Guid>.Assign(order, CombGuidGenerator.Instance);

        Assert.NotEqual(Guid.Empty, order.Id);
    }

    [Fact]
    public void Caller_supplied_generator_defines_what_empty_means()
    {
        var generator = new NeverEmptyGenerator();
        var order = new Order();

        IdAssigner<Order, Guid>.Assign(order, generator);

        Assert.Equal(Guid.Empty, order.Id);
        Assert.Equal(0, generator.GenerateCount);
    }

    [Fact]
    public void Comb_guid_generator_produces_distinct_usable_keys()
    {
        // CombGuidGenerator is the generator worth choosing for Guid keys, because it embeds a timestamp
        // and so keeps inserts clustered at one end of the _id index instead of scattering them. This
        // asserts only that it is wired up and usable; the index-locality claim is a documented rationale,
        // not a behaviour of this library.
        var orders = new List<Order> { new(), new(), new() };

        IdAssigner<Order, Guid>.Assign(orders, CombGuidGenerator.Instance);

        Assert.All(orders, order => Assert.NotEqual(Guid.Empty, order.Id));
        Assert.Equal(3, orders.Select(o => o.Id).Distinct().Count());
    }

    private sealed class NeverEmptyGenerator : IIdGenerator
    {
        public int GenerateCount { get; private set; }

        public object GenerateId(object container, object document)
        {
            GenerateCount++;
            return Guid.NewGuid();
        }

        public bool IsEmpty(object id) => false;
    }
}
