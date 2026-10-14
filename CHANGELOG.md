# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.0.8]

A correctness and performance release. It contains breaking changes; every one has a migration below.

### Fixed

- **Write concern was being silently downgraded on every insert.** `OperationTimeout` defaulted to 30
  seconds and was turned into `new WriteConcern(wTimeout: ...)`, which carries no `w` value. Applying it
  with `WithWriteConcern` replaced whatever the connection string specified, so a configured
  `w=majority&journal=true` became the server default. It also achieved nothing, because the server ignores
  `wtimeout` unless `w` is greater than 1. Write concern is now only ever set when
  `MongoRepositoryOptions.WriteConcern` is assigned explicitly.
- **`OperationTimeout` could not be configured.** `MongoRepositoryOptions` exposed `init`-only properties,
  so the `configureOptions` delegate on `AddMongoRepository` could not assign anything: any attempt was the
  compile error CS8852. Every option is now settable.
- **An ascending and a descending index on the same field collided.** Both were named after the field
  alone, and index creation deduplicated by name, so only the first was ever created. Names now follow the
  server's own convention and include direction: `Total_1` and `Total_-1`.
- **An index on a nested property indexed the wrong field.** The member name was read off the expression by
  hand, so `x => x.Address.City` created an index on a root-level `City` that no document has. Selectors now
  go through the driver's field resolver, which yields `Address.city`.
- **A changed index definition was silently ignored.** Comparison was by name only. Definitions are now
  compared by rendered keys, uniqueness, sparseness and TTL; a mismatch is logged, and
  `RecreateChangedIndexes` will drop and rebuild it.
- **`InsertMany` wrote null keys for string-keyed documents.** Keys were only generated on the
  single-document path, and the driver registers no generator for a `string` key, so every document went in
  with `_id: null` and the second one failed with a duplicate key error. Keys are now generated for batches
  too.
- **A document with an `int` key could not be used at all.** The constructor threw for any key type without
  a generator, even when the caller always supplied the key. A missing generator is no longer an error; the
  caller simply owns the key.
- **One transient fault broke a repository permanently.** Index creation ran inside a
  `Lazy<T>` with `LazyThreadSafetyMode.ExecutionAndPublication`, which caches the exception forever. A
  failed attempt is no longer recorded as complete, so the next call retries.
- **The package dropped one of its two READMEs.** `README.md` and `Core/README.md` were both packed to
  `\README.md` (NU5118). `Core/README.md` is gone; it documented an API that never existed.
- **Two vulnerable transitive dependencies.** MongoDB.Driver 3.4.0 brought in Snappier 1.0.0
  (GHSA-pggp-6c3x-2xmx, high) and SharpCompress 0.30.1 (GHSA-6c8g-7p36-r338, moderate). The driver is now
  3.12.0, and CI fails on any vulnerable package, transitive ones included.

### Added

- `CancellationToken` on every asynchronous method.
- Partial updates: `UpdateByIdAsync`, `UpdateOneAsync`, `UpdateManyAsync`, `FindOneAndUpdateAsync`, all with
  optional upsert.
- `ReplaceOneAsync`, and `BulkWriteAsync` for a mixed batch in one round-trip.
- `StreamAsync`, returning `IAsyncEnumerable<T>`, which holds one server batch and releases its cursor even
  when the consumer breaks out early.
- `GetPageAsync`, returning `PagedResult<T>` with the total count, page count and navigation flags.
- `ExistsAsync`, which stops at the first match; `EstimateCountAsync`, a constant-time count from collection
  metadata; `DistinctAsync`.
- `FindOneAndDeleteAsync`, for when the deleted document is actually needed.
- `Collection`, exposing the underlying `IMongoCollection<TDocument>` for aggregation pipelines, change
  streams and anything else the interface does not cover.
- `StartSessionAsync`, so a transaction can span several repositories.
- A non-generic `WithTransactionAsync` overload, and `TransactionOptions` on both.
- `EnsureIndexesAsync`, and `IndexCreationMode` to choose between `Startup`, `FirstUse` and `Disabled`.
- Index shapes: `TimeToLive`, `Text`, `Hashed`, `Custom`, and a `configure` hook reaching all of
  `CreateIndexOptions` for sparse indexes, partial filters, collations.
- Registration overloads for an `IMongoDatabase` already in the container, and for one resolved at run time.
  `AddMongoClient` registers the client on its own. `ServiceLifetime` is selectable.
- Driver diagnostics are routed into the application's `ILoggerFactory` when one is registered.
- `net8.0` and `net10.0` targets alongside `net9.0`.
- SourceLink, so the published `.snupkg` can step into library code.

### Changed

- **Indexes are created once at start-up**, by a hosted service, instead of from the repository's lazy
  initialiser. Combined with a scoped registration, the old arrangement meant a `listIndexes` round-trip on
  every HTTP request.
- **One `IMongoClient` per connection string**, registered as a keyed singleton, instead of one per
  repository instance. The connection pool is shared, and a transaction can span collections.
- **Deletes return the number of documents removed** instead of `void`, and use `deleteOne` / `deleteMany`
  instead of `findOneAndDelete`, so a deleted document is no longer sent back over the wire to be discarded.
- **Server failures are no longer wrapped.** A duplicate key stays a `MongoWriteException` and a server
  timeout stays a `MongoExecutionTimeoutException`, because callers need the driver's error codes.
  `RepositoryException` is now only for faults this library detects. The previous README described behaviour
  the code never had.
- **`OperationTimeout` defaults to `null`** and applies only to reads and counts, as `maxTimeMS`. Use a
  `CancellationToken` for client-side deadlines.
- The test suite was replaced. It previously mocked `IMongoRepository` and asserted that the mock returned
  what it had been configured to return, which exercised none of the library. There are now unit tests over
  index naming, key generation and registration, plus integration tests against a real MongoDB replica set
  via Testcontainers.

### Removed

- `AllAsync`, which returned a raw `IAsyncCursor<T>` that callers had to remember to dispose. Use
  `StreamAsync`, `FilterByAsync` or `GetPageAsync`.
- `CompoundHelper<TDocument>`, an unused internal duplicate of `MongoIndexes.Field`.
- `GuidIDGenerator`, a reimplementation of the driver's own `GuidGenerator`.
- The `Microsoft.SourceLink.GitHub` package reference. SourceLink ships in the SDK from .NET 8, and the
  package pulls `Microsoft.Build.Tasks.Git`, which carries an advisory.

### Migration from 0.0.x

| Before | After |
| --- | --- |
| `MongoDocument<T, TKey>.CreateAscendingIndex(x => x.F)` | `MongoIndexes<T, TKey>.Ascending(x => x.F)` |
| `MongoDocument<T, TKey>.CreateDescendingIndex(x => x.F)` | `MongoIndexes<T, TKey>.Descending(x => x.F)` |
| `MongoDocument<T, TKey>.CreateCompoundIndex(false, a, b)` | `MongoIndexes<T, TKey>.Compound(a, b)` |
| `AddMongoRepository<T, K>(cs, db, indexes: [...])` | `AddMongoRepository<T, K>(cs, db, o => o.AddIndex(...))` |
| `await (await repo.AllAsync()).ToListAsync()` | `await foreach (var x in repo.StreamAsync())` |
| `repo.AsyncCount(filter)` | `repo.CountAsync(filter, cancellationToken: token)` |
| `repo.FilterBy(filter)` returning a lazy cursor | `await repo.FilterByAsync(filter, cancellationToken: token)` |
| `repo.DeleteById(id)` returning `void` | `await repo.DeleteByIdAsync(id, cancellationToken: token)`, returning a count |
| `repo.FindOneAndReplace(doc)` | `await repo.ReplaceOneAsync(doc, cancellationToken: token)` |
| `repo.WithTransactionAsync(session => ...)` | `repo.WithTransactionAsync((session, ct) => ...)` |
| `IDocument<TKey>.Id` typed `TKey?` | typed `TKey`, with `TKey : notnull` |
| Namespaces `.Interface`, `.Attributes`, `.Exceptions`, `.Configuration` | all in `SharpMongoRepository` |
| Namespace `.Enum`, `.Compound` | `SharpMongoRepository.Indexing` |

`MongoDocument<T, TKey>` and the old `AddMongoRepository` overload remain as `[Obsolete]` forwarders so the
compiler points at the replacement. Both will be removed in 0.1.0.

One consequence of keeping that registration overload: a literal `null` in the fourth argument position is
now ambiguous between it and the current `configure` delegate (CS0121). Either drop the argument, or state
the type: `(List<MongoIndex<T, TKey>>?)null`.

One migration note worth calling out: because index names now include direction, the names your collections
already carry will not match. Existing indexes are left in place, and the correctly named ones are created
alongside them. Drop the old ones once you have confirmed nothing depends on them.

## [0.0.7] and earlier

See the commit history. Note that `Core/SharpMongoRepository.csproj` declared `0.0.6` while `0.0.7` was
published, because releases were being made outside CI; the publish workflow now refuses to run when the
tag and the project file disagree.
