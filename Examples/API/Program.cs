using API;
using API.Extensions;
using MongoDB.Driver;
using SharpMongoRepository;
using SharpMongoRepository.Enum;
using SharpMongoRepository.Interface;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<MongoSettings>(builder.Configuration.GetSection("Mongo"));

builder.Services.AddMongoRepository([
    MongoDocument<WeatherForecast, Guid>.CreateAscendingIndex(x => x.Date, false),
    MongoDocument<WeatherForecast, Guid>.CreateCompoundIndex(
        unique: false,
        MongoDocument<WeatherForecast, Guid>.Field(IndexDirection.Ascending, x => x.Date),
        MongoDocument<WeatherForecast, Guid>.Field(IndexDirection.Descending, x => x.TemperatureC)
    )
]);

builder.Services.AddSwaggerDocumentation();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapGet("/weatherforecast", async (IMongoRepository<WeatherForecast, Guid> repository) => 
{
    var forecasts = await repository.AllAsync();

    return Results.Ok(await forecasts.ToListAsync());
}).WithName("WeatherForecast");

app.MapGet("/weatherforecast/{id}", async (Guid id, IMongoRepository<WeatherForecast, Guid> repository) =>
    {
        var forecast = await repository.FindByIdAsync(id);

        return forecast is not null
            ? Results.Ok(forecast)
            : Results.NotFound();
    })
    .WithName("GetWeatherForecastById")
    .WithOpenApi();

app.MapPost("/weatherforecast", async (
        WeatherForecast forecast,
        IMongoRepository<WeatherForecast, Guid> repository) =>
{
    await repository.InsertOneAsync(forecast);

    return Results.Created($"/weatherforecast/{forecast.Id.ToString()}", forecast);
})
    .WithName("CreateWeatherForecast")
    .WithOpenApi();

app.MapPost("/manyFeatherforecast", async (
        IList<WeatherForecast> forecast,
        IMongoRepository<WeatherForecast, Guid> repository) =>
{
    await repository.InsertManyAsync(forecast);

    return Results.Ok();
})
    .WithName("ManyCreateWeatherForecast")
    .WithOpenApi();

app.MapDelete("/weatherforecast/{id}", async (
        Guid id,
        IMongoRepository<WeatherForecast, Guid> repository) =>
    {
        var forecast = await repository.FindByIdAsync(id);

        if (forecast is null)
            return Results.NotFound();

        await repository.DeleteByIdAsync(id, null);

        return Results.NoContent();
    })
    .WithName("DeleteWeatherForecast")
    .WithOpenApi();


app.Run();