# Contributing

Contributions are welcome. This document covers what you need to build and test the project, and what a
reviewable change looks like.

## Prerequisites

- .NET SDK 10.0 or later. The library targets `net8.0`, `net9.0` and `net10.0`; to run the test suite
  across all three you need all three runtimes installed.
- Docker, for the integration tests. They are skippable, see below.
- Git.

## Building and testing

```bash
git clone https://github.com/<your-username>/SharpMongoRepository.git
cd SharpMongoRepository

dotnet restore
dotnet build

# Everything. Needs Docker: Testcontainers starts a single-node MongoDB replica set.
dotnet test

# Unit tests only, no Docker needed. Covers index naming, key generation and registration.
dotnet test --filter "Category!=Integration"

# Integration tests only, on one target framework.
dotnet test Tests/Tests.csproj --framework net10.0 --filter "Category=Integration"

# One target framework, if you do not have all three runtimes.
dotnet test --framework net10.0
```

Before opening a pull request, run what CI runs:

```bash
dotnet format --verify-no-changes
dotnet build --configuration Release -p:ContinuousIntegrationBuild=true
dotnet test --configuration Release
dotnet list package --vulnerable --include-transitive
```

`ContinuousIntegrationBuild=true` turns warnings into errors, which is how the build runs in CI. If it
passes locally with that flag, it will pass there.

## Running the example

```bash
cd Examples/API
docker compose up -d
dotnet run
```

The compose file starts a single-node replica set, so the transaction endpoint works.

## Project layout

| Path | Contents |
| --- | --- |
| `Core/` | The library. `MongoRepository`, `IMongoRepository`, options, and the `Indexing` and `Internal` namespaces. |
| `Tests/Unit/` | Tests that need no server: index rendering and naming, key generation, registration and options. |
| `Tests/Integration/` | Tests against a real MongoDB replica set, tagged `Category=Integration`. |
| `Examples/API/` | A minimal API exercising the public surface. |
| `Directory.Packages.props` | Every package version. Central package management is on, so versions live here, not in the project files. |
| `Directory.Build.props` | Settings shared by all projects: analyzers, nullability, determinism. |
| `.editorconfig` | Code style, enforced at build time. |

## Code style

Style is enforced by `.editorconfig` and checked by `dotnet format` in CI, so there is little to remember:

- File-scoped namespaces, `var` where the type is evident, expression bodies for one-line members.
- Private fields are `_camelCase`; `static readonly` fields and constants are `PascalCase`.
- Public members carry XML documentation. `GenerateDocumentationFile` is on and the missing-documentation
  warning is not suppressed, so the build tells you when you have forgotten.
- When you suppress an analyzer, say why in the same place you suppress it.

## Writing tests

Every behavioural change needs a test, and the useful distinction is which kind:

- **Unit tests** for anything decidable without a server: an index's rendered keys and generated name, which
  generator a key type resolves to, what a registration puts in the container, which options reach the
  collection. These are the tests that caught the index naming collision and the nested-path bug.
- **Integration tests** for anything that depends on the server's own behaviour: whether a filter actually
  matches, whether an index is enforced, whether a transaction rolls back. Key serialization is the clearest
  case: addressing `_id` as a plain field name looks correct and passes every unit test, but throws for a
  `Guid` key against a real server. Only an integration test catches that.

A test that asserts a mock returns what it was configured to return is not a test. The suite this project
replaced consisted entirely of those, and none of the nine defects fixed in 0.0.8 tripped it.

Integration tests derive from the `mongodb` collection so they share one container, and each class works in
its own database via `MongoFixture.NewDatabaseName()`, so they can run in any order.

## Pull requests

- Branch from `main`: `git checkout -b feature/short-description`.
- Keep a pull request to one concern. A bug fix and a refactor are two pull requests.
- Describe what changed and why. If it fixes a defect, say how the defect showed up.
- Note any breaking change explicitly and add it to `CHANGELOG.md` with its migration.
- Public API changes should keep an `[Obsolete]` forwarder for one minor version where that is practical.

## Reporting a bug

Include:

- What you expected and what happened instead.
- The versions of SharpMongoRepository, the .NET runtime and the MongoDB server.
- The smallest code sample that reproduces it, including the document type and its attributes. Key type and
  `[BsonRepresentation]` matter more often than you would think.
- The full exception, with its stack trace.

For a suspected security issue, do not open an issue. See [SECURITY.md](SECURITY.md).

## Releasing

Maintainers only:

1. Update `<Version>` in `Core/SharpMongoRepository.csproj`.
2. Add the release section to `CHANGELOG.md`.
3. Tag `v<version>` and publish a GitHub release.

The publish workflow refuses to run when the tag and the project file disagree, which is how the 0.0.6
project file came to be published as 0.0.7.

## License

Contributions are accepted under the [MIT License](LICENSE).
