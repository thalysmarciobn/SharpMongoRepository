using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using SharpMongoRepository;

namespace Tests;

/// <summary>
///     A Guid-keyed document whose key is stored as a string, and which has a nested object and a renamed
///     field. The shape deliberately exercises the three things index and key handling used to get wrong.
/// </summary>
[BsonCollection("orders")]
public sealed class Order : IDocument<Guid>
{
    [BsonRepresentation(BsonType.String)]
    public Guid Id { get; set; }

    [BsonElement("customer_email")]
    public string CustomerEmail { get; set; } = string.Empty;

    public int Total { get; set; }

    public string Status { get; set; } = "pending";

    public DateTime CreatedAt { get; set; }

    public Address? Address { get; set; }
}

public sealed class Address
{
    [BsonElement("city")]
    public string City { get; set; } = string.Empty;

    public string Country { get; set; } = string.Empty;
}

/// <summary>A string-keyed document: the case the driver registers no identifier generator for.</summary>
[BsonCollection("tickets")]
public sealed class Ticket : IDocument<string>
{
    public string Id { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;
}

/// <summary>An ObjectId-keyed document.</summary>
[BsonCollection("events")]
public sealed class Event : IDocument<ObjectId>
{
    public ObjectId Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

/// <summary>An int-keyed document: no generator exists, so the caller assigns identifiers.</summary>
[BsonCollection("counters")]
public sealed class Counter : IDocument<int>
{
    public int Id { get; set; }

    public long Value { get; set; }
}

/// <summary>Deliberately missing <see cref="BsonCollectionAttribute" />.</summary>
public sealed class Undeclared : IDocument<Guid>
{
    public Guid Id { get; set; }
}
