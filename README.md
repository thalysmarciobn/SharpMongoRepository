# SharpMongoRepository

**SharpMongoRepository** is a lightweight, high-performance MongoDB repository pattern implementation for .NET, designed to simplify CRUD operations, index management, and transactions with clean, fluent APIs.

---

## 🚀 Features

- ✅ **Full CRUD Support** – Sync/Async operations  
- ✅ **Automatic Index Management** – Define indexes via attributes or fluent API  
- ✅ **LINQ & Lambda Support** – Expressive querying  
- ✅ **Transaction Support** – ACID-compliant operations  
- ✅ **Flexible ID Types** – `ObjectId`, `Guid`, `string` or `custom` 
- ✅ **DI-Friendly** – Easy integration with `IServiceCollection`

---

## 📦 Installation

```bash
dotnet add package SharpMongoRepository
```

---

## 📝 Usage Examples

### 1. Configure MongoDB Settings (e.g., `appsettings.json`)

```json
{
  "MongoSettings": {
    "ConnectionString": "mongodb://localhost:27017",
    "Database": "ExampleDb"
  }
}
```

### 2. Register Services

```csharp
// Program.cs
builder.Services.Configure<MongoSettings>(builder.Configuration.GetSection("MongoSettings"));

// Register repository with optional indexes
builder.Services.AddMongoRepository<WeatherForecast, Guid>(
    indexes: new List<MongoIndex<WeatherForecast, Guid>>
    {
        MongoDocument<WeatherForecast, Guid>.CreateAscendingIndex(x => x.Date),
        MongoDocument<WeatherForecast, Guid>.CreateCompoundIndex(
            unique: false,
            MongoDocument<WeatherForecast, Guid>.Field(IndexDirection.Ascending, x => x.Date),
            MongoDocument<WeatherForecast, Guid>.Field(IndexDirection.Descending, x => x.TemperatureC)
        )
    }
);
```

### 3. Define a Document Model

```csharp
[BsonCollection("weatherForecasts")] // MongoDB collection name
public class WeatherForecast : IDocument<Guid>
{
    [BsonId]
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime Date { get; set; }
    public int TemperatureC { get; set; }
    public string? Summary { get; set; }
}
```

### 4. Minimal API Endpoints

```csharp
// GET all
app.MapGet("/weatherforecast", async (IMongoRepository<WeatherForecast, Guid> repo) =>
    Results.Ok(await repo.AllAsync().ToListAsync()));

// GET by ID
app.MapGet("/weatherforecast/{id}", async (Guid id, IMongoRepository<WeatherForecast, Guid> repo) =>
    await repo.FindByIdAsync(id) is { } forecast
        ? Results.Ok(forecast)
        : Results.NotFound());

// POST
app.MapPost("/weatherforecast", async (WeatherForecast forecast, IMongoRepository<WeatherForecast, Guid> repo) =>
{
    await repo.InsertOneAsync(forecast);
    return Results.Created($"/weatherforecast/{forecast.Id}", forecast);
});

// DELETE
app.MapDelete("/weatherforecast/{id}", async (Guid id, IMongoRepository<WeatherForecast, Guid> repo) =>
{
    await repo.DeleteByIdAsync(id);
    return Results.NoContent();
});
```

### 5. Advanced Queries

```csharp
// Filter with projection
var coldDays = repo.FilterBy(
    x => x.TemperatureC < 10,
    x => new { x.Date, x.Summary }
);

// Transaction
await repo.WithTransactionAsync(async session =>
{
    await repo.InsertOneAsync(new WeatherForecast { ... }, session);
    await repo.DeleteManyAsync(x => x.TemperatureC > 30, session);
    return "Operation succeeded";
});
```

---

## 📜 Index Management

Define indexes during registration or dynamically:

```csharp
// Single field
MongoDocument<WeatherForecast, Guid>.CreateAscendingIndex(x => x.Date, unique: true);

// Compound index
MongoDocument<WeatherForecast, Guid>.CreateCompoundIndex(
    unique: true,
    MongoDocument<WeatherForecast, Guid>.Field(IndexDirection.Ascending, x => x.Date),
    MongoDocument<WeatherForecast, Guid>.Field(IndexDirection.Descending, x => x.Summary)
);
```

---

## ⚠️ Error Handling

Custom exceptions (`RepositoryException`) are thrown for:

- Connection failures
- Invalid operations (e.g., duplicate keys)
- Timeouts (configurable via `MongoRepositoryOptions`)

```csharp
try
{
    await repo.InsertOneAsync(document);
}
catch (RepositoryException ex)
{
    logger.LogError(ex, "MongoDB operation failed");
}
```

---

## 🌟 Why Use This?

- **No Boilerplate:** Focus on business logic, not MongoDB driver intricacies.
- **Thread-Safe:** Lazy-loaded clients and collections.


[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

