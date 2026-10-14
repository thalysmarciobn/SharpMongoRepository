using MongoDB.Driver;
using Testcontainers.MongoDb;
using Xunit;

namespace Tests.Integration;

/// <summary>
///     Starts a throwaway MongoDB replica set for the integration suite.
/// </summary>
/// <remarks>
///     <para>
///         A single-node replica set rather than a standalone server, because transactions need one. The
///         container is shared by every test in <see cref="MongoCollectionFixture" />, and each test class
///         works in its own database so they cannot interfere with one another.
///     </para>
///     <para>
///         Requires a working Docker daemon. Without one, run <c>dotnet test</c> with
///         <c>--filter Category!=Integration</c>; the unit suite covers index naming, key generation and
///         registration on its own.
///     </para>
/// </remarks>
public sealed class MongoFixture : IAsyncLifetime
{
    private readonly MongoDbContainer _container = new MongoDbBuilder()
        .WithImage("mongo:7.0")
        .WithReplicaSet()
        .Build();

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // The replica set advertises its members by their in-container address (127.0.0.1:27017), which
        // from the host resolves to nothing: the container's port is published on a different one. Left
        // alone, the driver discovers that member through normal topology monitoring, fails to reach it,
        // and every operation waits out server selection. directConnection=true turns discovery off and
        // keeps the driver talking to the published address. Transactions still work, because the server
        // is a replica set member.
        ConnectionString = new MongoUrlBuilder(_container.GetConnectionString())
        {
            DirectConnection = true
        }.ToMongoUrl().ToString();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <summary>Creates a client against the container.</summary>
    public IMongoClient CreateClient()
        => new MongoClient(MongoClientSettings.FromConnectionString(ConnectionString));

    /// <summary>Creates a client whose settings are adjusted before use.</summary>
    public IMongoClient CreateClient(Action<MongoClientSettings> configure)
    {
        var settings = MongoClientSettings.FromConnectionString(ConnectionString);
        configure(settings);
        return new MongoClient(settings);
    }

    /// <summary>Returns a database name unique to the caller, so test classes stay isolated.</summary>
    public static string NewDatabaseName() => $"test_{Guid.NewGuid():N}";
}

/// <summary>Shares one container across every integration test class.</summary>
[CollectionDefinition(Name)]
public sealed class MongoCollectionFixture : ICollectionFixture<MongoFixture>
{
    public const string Name = "mongodb";
}
