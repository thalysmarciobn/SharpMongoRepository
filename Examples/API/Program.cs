using API.Extensions;
using API.Models;
using MongoDB.Driver;
using SharpMongoRepository;
using SharpMongoRepository.Extensions;
using SharpMongoRepository.Indexing;

var builder = WebApplication.CreateBuilder(args);

var mongo = builder.Configuration.GetSection("Mongo");

var connectionString = mongo["ConnectionString"]
                       ?? throw new InvalidOperationException(
                           "Configuration 'Mongo:ConnectionString' is missing.");

var databaseName = mongo["Database"]
                   ?? throw new InvalidOperationException(
                       "Configuration 'Mongo:Database' is missing.");

// One registration call. It adds a singleton IMongoClient keyed by the connection string, a scoped
// repository, and a hosted service that creates the indexes below once, before the first request.
builder.Services.AddMongoRepository<WeatherForecast, Guid>(
    connectionString,
    databaseName,
    options => options
        .AddIndex(MongoIndexes<WeatherForecast>.Ascending(x => x.Date))
        .AddIndex(MongoIndexes<WeatherForecast>.Compound(
            MongoIndexes<WeatherForecast>.Field(IndexDirection.Ascending, x => x.City),
            MongoIndexes<WeatherForecast>.Field(IndexDirection.Descending, x => x.TemperatureC))),
    configureClient: settings => settings.MaxConnectionPoolSize = 100);

// A keyless repository: AuditEntry declares no key, no interface and no collection attribute, so the
// collection name is given here. Reads go through the composable query.
builder.Services.AddMongoRepository<AuditEntry>(
    connectionString,
    databaseName,
    collectionName: "audit");

builder.Services.AddSwaggerDocumentation();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// GET all, streamed. Memory stays flat no matter how many documents match, because only the current
// server batch is held rather than the whole result set.
app.MapGet("/forecasts", (IMongoRepository<WeatherForecast, Guid> repository, CancellationToken token)
        => repository.StreamAsync(cancellationToken: token))
    .WithName("StreamForecasts")
    .WithSummary("Streams every forecast.");

// GET one page, with the total count.
app.MapGet("/forecasts/page", async (
        IMongoRepository<WeatherForecast, Guid> repository,
        CancellationToken token,
        int page = 1,
        int pageSize = 20,
        string? city = null) =>
    {
        var result = await repository.GetPageAsync(
            page,
            pageSize,
            city is null ? null : x => x.City == city,
            Builders<WeatherForecast>.Sort.Descending(x => x.Date),
            cancellationToken: token);

        return Results.Ok(result);
    })
    .WithName("GetForecastPage")
    .WithSummary("Returns one page of forecasts, newest first.");

// GET a projection: only the fields asked for cross the wire.
app.MapGet("/forecasts/summaries", async (
        IMongoRepository<WeatherForecast, Guid> repository,
        CancellationToken token) =>
    {
        var summaries = await repository.FilterByAsync(
            x => x.Summary != null,
            x => new { x.City, x.Date, x.Summary },
            cancellationToken: token);

        return Results.Ok(summaries);
    })
    .WithName("GetForecastSummaries")
    .WithSummary("Returns a projection rather than whole documents.");

app.MapGet("/forecasts/cities", async (
        IMongoRepository<WeatherForecast, Guid> repository,
        CancellationToken token) =>
    Results.Ok(await repository.DistinctAsync(x => x.City, cancellationToken: token)))
    .WithName("GetForecastCities")
    .WithSummary("Lists the distinct cities on record.");

app.MapGet("/forecasts/{id:guid}", async (
        Guid id,
        IMongoRepository<WeatherForecast, Guid> repository,
        CancellationToken token) =>
    {
        var forecast = await repository.FindByIdAsync(id, cancellationToken: token);

        return forecast is not null ? Results.Ok(forecast) : Results.NotFound();
    })
    .WithName("GetForecastById")
    .WithSummary("Returns one forecast by its identifier.");

app.MapPost("/forecasts", async (
        ForecastRequest request,
        IMongoRepository<WeatherForecast, Guid> repository,
        CancellationToken token) =>
    {
        var forecast = request.ToDocument();

        await repository.InsertOneAsync(forecast, cancellationToken: token);

        return Results.Created($"/forecasts/{forecast.Id}", forecast);
    })
    .WithName("CreateForecast")
    .WithSummary("Creates one forecast; the key is generated and written back.");

app.MapPost("/forecasts/batch", async (
        IList<ForecastRequest> requests,
        IMongoRepository<WeatherForecast, Guid> repository,
        CancellationToken token) =>
    {
        var forecasts = requests.Select(r => r.ToDocument()).ToList();

        // One command for the whole batch, with keys generated for every document.
        await repository.InsertManyAsync(forecasts, ordered: false, cancellationToken: token);

        return Results.Ok(forecasts.Select(f => f.Id));
    })
    .WithName("CreateForecastBatch")
    .WithSummary("Creates many forecasts in a single round-trip.");

// PATCH: a partial update. Only the changed fields are sent, and a concurrent writer's changes to other
// fields survive, which a whole-document replacement would discard.
app.MapPatch("/forecasts/{id:guid}/temperature", async (
        Guid id,
        int temperatureC,
        IMongoRepository<WeatherForecast, Guid> repository,
        CancellationToken token) =>
    {
        var result = await repository.UpdateByIdAsync(
            id,
            Builders<WeatherForecast>.Update.Set(x => x.TemperatureC, temperatureC),
            cancellationToken: token);

        return result.MatchedCount == 0 ? Results.NotFound() : Results.NoContent();
    })
    .WithName("UpdateForecastTemperature")
    .WithSummary("Updates a single field without resending the document.");

app.MapPut("/forecasts/{id:guid}", async (
        Guid id,
        ForecastRequest request,
        IMongoRepository<WeatherForecast, Guid> repository,
        CancellationToken token) =>
    {
        var forecast = request.ToDocument();
        forecast.Id = id;

        var result = await repository.ReplaceOneAsync(forecast, cancellationToken: token);

        return result.MatchedCount == 0 ? Results.NotFound() : Results.Ok(forecast);
    })
    .WithName("ReplaceForecast")
    .WithSummary("Replaces a whole forecast.");

app.MapDelete("/forecasts/{id:guid}", async (
        Guid id,
        IMongoRepository<WeatherForecast, Guid> repository,
        CancellationToken token) =>
    {
        // The delete reports how many documents it removed, so no read is needed first.
        var deleted = await repository.DeleteByIdAsync(id, cancellationToken: token);

        return deleted == 0 ? Results.NotFound() : Results.NoContent();
    })
    .WithName("DeleteForecast")
    .WithSummary("Deletes one forecast.");

// The composable query: conditions, sort and paging in one find command, with the count skipped so an
// endless-scrolling list costs one round-trip instead of two.
app.MapGet("/forecasts/search", async (
        IMongoRepository<WeatherForecast, Guid> repository,
        CancellationToken token,
        string? city = null,
        int? minTemperature = null,
        int page = 1,
        int pageSize = 20) =>
    {
        var query = repository.Query();

        if (city is not null)
            query = query.Where(x => x.City == city);

        if (minTemperature is not null)
            query = query.Where(x => x.TemperatureC >= minTemperature);

        var result = await query
            .OrderByDescending(x => x.Date)
            .ThenBy(x => x.City)
            .ToPageAsync(page, pageSize, includeTotal: false, cancellationToken: token);

        return Results.Ok(result);
    })
    .WithName("SearchForecasts")
    .WithSummary("Composable search; conditions are added only when supplied.");

// Reads one field by key, without transferring the document.
app.MapGet("/forecasts/{id:guid}/summary", async (
        Guid id,
        IMongoRepository<WeatherForecast, Guid> repository,
        CancellationToken token) =>
    {
        var summary = await repository.QueryById(id).FirstOrDefaultAsync(x => x.Summary, token);

        return summary is null ? Results.NotFound() : Results.Ok(new { summary });
    })
    .WithName("GetForecastSummary")
    .WithSummary("Projects one field out of a document selected by key.");

// A keyless document type: no key, so reads are filters.
app.MapGet("/audit", (
        IMongoRepository<AuditEntry> repository,
        CancellationToken token,
        string? actor = null) =>
    {
        var query = repository.Query();

        if (actor is not null)
            query = query.Where(x => x.Actor == actor);

        return query.OrderByDescending(x => x.At).Take(100).ToAsyncEnumerable(token);
    })
    .WithName("StreamAuditEntries")
    .WithSummary("Streams audit entries; the type declares no key.");

app.MapPost("/audit", async (
        AuditEntry entry,
        IMongoRepository<AuditEntry> repository,
        CancellationToken token) =>
    {
        entry.At = DateTime.UtcNow;
        await repository.InsertOneAsync(entry, cancellationToken: token);

        return Results.Accepted();
    })
    .WithName("CreateAuditEntry")
    .WithSummary("Writes an audit entry; the server assigns its _id.");

// A transaction spanning two writes. Requires a replica set; the docker-compose file in this folder
// starts one.
app.MapPost("/forecasts/move-city", async (
        string fromCity,
        string toCity,
        IMongoRepository<WeatherForecast, Guid> repository,
        CancellationToken token) =>
    {
        var moved = await repository.WithTransactionAsync(async (session, ct) =>
        {
            var result = await repository.UpdateManyAsync(
                x => x.City == fromCity,
                Builders<WeatherForecast>.Update.Set(x => x.City, toCity),
                session,
                ct);

            return result.ModifiedCount;
        }, cancellationToken: token);

        return Results.Ok(new { moved });
    })
    .WithName("MoveForecastsBetweenCities")
    .WithSummary("Reassigns every forecast from one city to another, atomically.");

await app.RunAsync();
