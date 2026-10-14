# SharpMongoRepository

A MongoDB repository for .NET built on the official driver. It covers CRUD, partial updates, projections,
streaming reads, bulk writes, retrying transactions and declarative index management, without hiding the
driver when you need it.

[![NuGet](https://img.shields.io/nuget/v/SharpMongoRepository.svg)](https://www.nuget.org/packages/SharpMongoRepository)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

Targets `net8.0`, `net9.0` and `net10.0`.

## Installation

```bash
dotnet add package SharpMongoRepository
```

## Quick start

Define a document. The `[BsonCollection]` attribute names the collection; `IDocument<TKey>` requires a
settable `Id`, because the repository writes a generated key back onto the instance you pass in.

```csharp
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using SharpMongoRepository;

[BsonCollection("orders")]
public sealed class Order : IDocument<Guid>
{
    [BsonRepresentation(BsonType.String)]
    public Guid Id { get; set; }

    public string CustomerEmail { get; set; } = string.Empty;
    public int Total { get; set; }
    public string Status { get; set; } = "pending";
    public DateTime CreatedAt { get; set; }
}
```

Register it. One call adds a singleton `IMongoClient`, a scoped repository, and a hosted service that
creates the indexes once during start-up.

```csharp
using SharpMongoRepository.Extensions;
using SharpMongoRepository.Indexing;

builder.Services.AddMongoRepository<Order, Guid>(
    connectionString: "mongodb://localhost:27017",
    databaseName: "shop",
    options => options
        .AddIndex(MongoIndexes<Order, Guid>.Ascending(x => x.CustomerEmail, unique: true))
        .AddIndex(MongoIndexes<Order, Guid>.Compound(
            MongoIndexes<Order, Guid>.Field(IndexDirection.Ascending, x => x.Status),
            MongoIndexes<Order, Guid>.Field(IndexDirection.Descending, x => x.CreatedAt))));
```

Inject and use it.

```csharp
app.MapPost("/orders", async (
    Order order,
    IMongoRepository<Order, Guid> repository,
    CancellationToken token) =>
{
    await repository.InsertOneAsync(order, cancellationToken: token);
    return Results.Created($"/orders/{order.Id}", order);
});

app.MapGet("/orders/{id:guid}", async (
    Guid id,
    IMongoRepository<Order, Guid> repository,
    CancellationToken token) =>
        await repository.FindByIdAsync(id, cancellationToken: token) is { } order
            ? Results.Ok(order)
            : Results.NotFound());
```

## Reading

Pick the read that matches the size of the result, not the one that is shortest to type.

```csharp
// A single document.
var order = await repository.FindByIdAsync(id, cancellationToken: token);
var first = await repository.FindOneAsync(x => x.Status == "pending", cancellationToken: token);

// A bounded set, buffered into a list.
var pending = await repository.FilterByAsync(x => x.Status == "pending", cancellationToken: token);

// A projection. Only the fields you ask for cross the wire; this is the cheapest read available.
var summaries = await repository.FilterByAsync(
    x => x.Total > 100,
    x => new { x.CustomerEmail, x.Total },
    cancellationToken: token);

// An unbounded set, streamed. Memory stays flat regardless of how many documents match, and the cursor
// is released even if you break out of the loop early.
await foreach (var o in repository.StreamAsync(x => x.Total > 0, cancellationToken: token))
    Process(o);

// One page plus the total count.
var page = await repository.GetPageAsync(
    page: 2,
    pageSize: 50,
    filter: x => x.Status == "pending",
    sort: Builders<Order>.Sort.Descending(x => x.CreatedAt),
    cancellationToken: token);

// Existence, which stops at the first match rather than counting every one.
var any = await repository.ExistsAsync(x => x.CustomerEmail == email, cancellationToken: token);

// Counting. CountAsync is exact and scans; EstimateCountAsync reads collection metadata in constant
// time but cannot be filtered and may lag reality.
var exact = await repository.CountAsync(x => x.Status == "pending", cancellationToken: token);
var approximate = await repository.EstimateCountAsync(token);

var statuses = await repository.DistinctAsync(x => x.Status, cancellationToken: token);
```

## Writing

```csharp
// Insert. The key is generated when empty and written back onto the instance.
await repository.InsertOneAsync(order, cancellationToken: token);

// Insert many: one command for the batch. Pass ordered: false to let the server parallelise it and
// report all failures together instead of stopping at the first.
await repository.InsertManyAsync(orders, ordered: false, cancellationToken: token);

// Partial update: sends only the change, and leaves fields another writer touched alone.
await repository.UpdateByIdAsync(
    id,
    Builders<Order>.Update.Set(x => x.Status, "shipped").Inc(x => x.Revision, 1),
    cancellationToken: token);

// Same change across every match, in one command, without loading anything.
await repository.UpdateManyAsync(
    x => x.Status == "pending",
    Builders<Order>.Update.Set(x => x.Status, "cancelled"),
    cancellationToken: token);

// Atomic read-modify-write in a single command: the way to build a counter or claim a queue item.
var claimed = await repository.FindOneAndUpdateAsync(
    x => x.Status == "pending",
    Builders<Order>.Update.Set(x => x.Status, "processing"),
    cancellationToken: token);

// Whole-document replacement, when you do mean to overwrite every field.
await repository.ReplaceOneAsync(order, upsert: true, cancellationToken: token);

// Deletes report how many documents went, so no read is needed to tell a delete from a no-op.
var deleted = await repository.DeleteByIdAsync(id, cancellationToken: token);
var purged = await repository.DeleteManyAsync(x => x.CreatedAt < cutoff, cancellationToken: token);

// A mixed batch in one round-trip. A thousand writes in a loop costs a thousand round-trips; this
// costs one.
await repository.BulkWriteAsync(
[
    new UpdateOneModel<Order>(filter, update),
    new DeleteOneModel<Order>(otherFilter),
    new InsertOneModel<Order>(newOrder)
], cancellationToken: token);
```

## Transactions

`WithTransactionAsync` delegates to the driver's transaction runner, which retries the body on
`TransientTransactionError` and re-commits on `UnknownTransactionCommitResult`. The body may therefore run
more than once and must be idempotent in its non-database side effects.

```csharp
var shipped = await repository.WithTransactionAsync(async (session, ct) =>
{
    await repository.UpdateByIdAsync(
        id, Builders<Order>.Update.Set(x => x.Status, "shipped"), session: session, cancellationToken: ct);

    await repository.InsertOneAsync(auditEntry, session, ct);

    return 1;
}, cancellationToken: token);
```

To span several collections, start a session and pass it to each repository. Every repository registered
against the same connection string shares one `IMongoClient`, which is what makes this work:

```csharp
using var session = await orders.StartSessionAsync(token);

await session.WithTransactionAsync(async (handle, ct) =>
{
    await orders.InsertOneAsync(order, handle, ct);
    await invoices.InsertOneAsync(invoice, handle, ct);
    return true;
}, cancellationToken: token);
```

Transactions need a replica set or a sharded cluster. They are not available on a standalone server.

## Indexes

Index names follow the server's own convention, derived from the rendered keys, so direction is part of
the name and an ascending and a descending index on one field co-exist.

```csharp
MongoIndexes<Order, Guid>.Ascending(x => x.CustomerEmail, unique: true);   // customer_email_1
MongoIndexes<Order, Guid>.Descending(x => x.CreatedAt);                    // CreatedAt_-1
MongoIndexes<Order, Guid>.Ascending(x => x.Address.City);                  // Address.city_1

MongoIndexes<Order, Guid>.Compound(
    MongoIndexes<Order, Guid>.Field(IndexDirection.Ascending, x => x.Status),
    MongoIndexes<Order, Guid>.Field(IndexDirection.Descending, x => x.CreatedAt));

MongoIndexes<Order, Guid>.TimeToLive(x => x.CreatedAt, TimeSpan.FromDays(90));
MongoIndexes<Order, Guid>.Text(x => x.Notes);
MongoIndexes<Order, Guid>.Hashed(x => x.CustomerEmail);

// Sparse, partial filters and anything else on CreateIndexOptions.
MongoIndexes<Order, Guid>.Ascending(x => x.Total, configure: o => o.Sparse = true);
```

Nested paths and `[BsonElement]` renames resolve through the driver's own field resolver, so
`x => x.Address.City` indexes `Address.city` rather than a root-level `city`.

`IndexCreation` controls when they are created:

| Mode | Behaviour |
| --- | --- |
| `Startup` (default) | A hosted service creates them before the first request is served. |
| `FirstUse` | Created once, on the first operation that touches the collection. |
| `Disabled` | Never created; indexes are owned by migrations or infrastructure. |

An existing index whose definition no longer matches is reported through the logger and left alone, because
rebuilding a large index is expensive and blocking. Set `RecreateChangedIndexes` to drop and rebuild it
instead.

## Configuration

```csharp
builder.Services.AddMongoRepository<Order, Guid>(
    connectionString,
    databaseName,
    options =>
    {
        options.AddIndex(MongoIndexes<Order, Guid>.Ascending(x => x.CustomerEmail));

        // A server-side execution cap, sent as maxTimeMS. For client-side deadlines, pass a
        // CancellationToken to the operation instead.
        options.OperationTimeout = TimeSpan.FromSeconds(10);

        // Left null, the connection string's own concerns are kept untouched.
        options.WriteConcern = WriteConcern.WMajority.With(journal: true);
        options.ReadConcern = ReadConcern.Majority;
        options.ReadPreference = ReadPreference.SecondaryPreferred;

        // Recommended for Guid keys: a COMB GUID embeds a timestamp, so inserts cluster at one end of
        // the _id index instead of scattering across it and splitting pages.
        options.IdGenerator = CombGuidGenerator.Instance;

        options.IndexCreation = IndexCreationMode.Startup;
        options.RecreateChangedIndexes = false;
    },
    configureClient: settings =>
    {
        settings.MaxConnectionPoolSize = 200;
        settings.Compressors = [new CompressorConfiguration(CompressorType.Snappy)];
    });
```

Other registration shapes:

```csharp
// Over an IMongoDatabase already in the container.
builder.Services.AddMongoRepository<Order, Guid>();

// Over a database resolved at run time, for instance from a secret store.
builder.Services.AddMongoRepository<Order, Guid>(
    sp => sp.GetRequiredService<IMongoClient>().GetDatabase(ResolveTenantDatabase(sp)));

// A singleton repository. Safe: the type is stateless and the driver is thread-safe.
builder.Services.AddMongoRepository<Order, Guid>(
    connectionString, databaseName, lifetime: ServiceLifetime.Singleton);
```

Outside a host, construct one directly:

```csharp
var repository = new MongoRepository<Order, Guid>(connectionString, "shop");
await repository.EnsureIndexesAsync();
```

When an `ILoggerFactory` is registered, it is handed to the driver, so cluster, connection and command
events land in your own logging pipeline.

## The driver is not hidden

`Collection` exposes the underlying `IMongoCollection<TDocument>` for anything this interface does not
cover: aggregation pipelines, change streams, `$geoNear`, per-call read preferences.

```csharp
var byStatus = await repository.Collection
    .Aggregate()
    .Group(x => x.Status, g => new { Status = g.Key, Count = g.Count() })
    .ToListAsync(token);
```

## Error handling

`RepositoryException` covers faults this library detects: a missing `[BsonCollection]`, an unparsable
connection string, an index definition without a name.

Server-side failures are deliberately not wrapped. A duplicate key stays a `MongoWriteException`, a server
timeout stays a `MongoExecutionTimeoutException`, and an authentication failure stays a
`MongoAuthenticationException`, because you need the driver's error codes to decide whether to retry.

```csharp
try
{
    await repository.InsertOneAsync(order, cancellationToken: token);
}
catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
{
    return Results.Conflict();
}
```

## Performance notes

The design choices that matter under load, and why:

- **One `IMongoClient` per connection string**, registered as a singleton. The connection pool is shared
  by every repository, and a session started from one can span the others in a transaction.
- **Indexes created once at start-up**, not per request. Index management never touches the request path.
- **The `_id` filter is a cached expression field definition.** A key lookup no longer rebuilds the
  filter's expression tree on every call. It stays an expression rather than the plain field name `"_id"`
  on purpose: a plain name resolves no member, so the key would be serialized with the default serializer
  for its type, which throws for a `Guid`.
- **The collection name and `_id` field live in `static readonly` fields** on the closed generic type, so
  the runtime initialises them once per document type and the attribute reflection never recurs.
- **Emptiness checks on keys go through `EqualityComparer<TKey>.Default`**, which the JIT devirtualises
  and inlines for value-type keys, avoiding the boxing `IIdGenerator.IsEmpty(object)` forces.
- **`StreamAsync` holds one server batch**, so a scan of a large collection does not grow the heap with
  the result set.
- **Deletes use `deleteOne`/`deleteMany`**, not `findOneAndDelete`, so a deleted document is not sent back
  over the wire to be discarded.
- **`params ReadOnlySpan<T>`** on the index factory, so declaring indexes allocates no arrays. On
  `net8.0` this relies on an internal `ParamCollectionAttribute` polyfill.
- **`System.Threading.Lock`** on `net9.0` and above, falling back to a plain object monitor on `net8.0`.
- **`ConfigureAwait(false)`** throughout, so no synchronization context is captured.

Two things the library deliberately does not claim: it is **not trim- or AOT-safe**, because BSON class
mapping is reflection-driven, and it does **not** use `ValueTask`, because every operation here is a real
network round-trip that will not complete synchronously.

## Running the example

```bash
cd Examples/API
docker compose up -d        # single-node replica set, so transactions work
dotnet run
```

Then open `http://localhost:5081/swagger`, or use `API.http`.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). In short:

```bash
dotnet test                                     # everything, needs Docker
dotnet test --filter Category!=Integration      # unit tests only, no Docker needed
```

## License

MIT. See [LICENSE](LICENSE).
