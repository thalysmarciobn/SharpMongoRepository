## Overview

A complete MongoDB repository pattern implementation for .NET with async support, LINQ queries, and index management.

---

## Installation

```bash
dotnet add package SharpMongoRepository
```

---

## Document Model Example

```csharp
using MongoDB.Bson;
using SharpMongoRepository.Attributes;
using SharpMongoRepository.Interface;

[BsonCollection("temperature")]
public record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary) : IDocument
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
    public ObjectId Id { get; set; }
}
```

---

## Dependency Injection Setup

```csharp
var builder = WebApplication.CreateBuilder(args);

// Load settings from appsettings.json
builder.Services.Configure<MongoSettings>(builder.Configuration.GetSection("Mongo"));

// Register repository
builder.Services.AddMongoRepository<WeatherForecast>([
    new MongoIndex<WeatherForecast> {
        Keys = Builders<WeatherForecast>.IndexKeys.Ascending(x => x.Date),
        Options = new CreateIndexOptions { Unique = true }
    }
]);
```

---

## Practical Usage Example

```csharp
using Microsoft.Extensions.DependencyInjection;
using SharpMongoRepository.Interface;

var services = new ServiceCollection();
services.Configure<MongoSettings>(config.GetSection("Mongo"));
services.AddMongoRepository<WeatherForecast>();
var provider = services.BuildServiceProvider();

// Get repository
var repository = provider.GetRequiredService<IMongoRepository<WeatherForecast>>();

// Insert
var forecast = new WeatherForecast(DateOnly.FromDateTime(DateTime.UtcNow), 25, "Sunny");
await repository.InsertOneAsync(forecast);

// Find by ID
var found = await repository.FindByIdAsync(forecast.Id.ToString());

// Find with predicate
var coldDays = await repository.FindAsync(x => x.TemperatureC < 10).ToListAsync();

// Replace (update)
var updated = forecast with { TemperatureC = 30 };
await repository.ReplaceOneAsync(updated);

// Delete
await repository.DeleteByIdAsync(forecast.Id.ToString());

// Count
var count = await repository.CountAsync();

// Check existence
var exists = await repository.ExistsAsync(x => x.Summary == "Sunny");

// Get all
var all = await repository.AllAsync();
var list = await all.ToListAsync();
```

---

## License

MIT
