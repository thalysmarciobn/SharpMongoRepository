using API;
using API.Extensions;
using MongoDB.Driver;
using SharpMongoRepository;
using SharpMongoRepository.Interface;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<MongoSettings>(builder.Configuration.GetSection("Mongo"));

builder.Services.AddMongoRepository<WeatherForecast>([
    MongoDocument<WeatherForecast>.CreateAscendingIndex(x => x.Date, true)
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

app.MapGet("/weatherforecast", async (IMongoRepository<WeatherForecast> repository) => 
{
    var forecasts = await repository.AllAsync();

    return Results.Ok(await forecasts.ToListAsync());
}).WithName("WeatherForecast");

app.MapGet("/weatherforecast/{id}", async (string id, IMongoRepository<WeatherForecast> repository) =>
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
        IMongoRepository<WeatherForecast> repository) =>
    {
        await repository.InsertOneAsync(forecast);

        return Results.Created($"/weatherforecast/{forecast.Id.ToString()}", forecast);
    })
    .WithName("CreateWeatherForecast")
    .WithOpenApi();

app.Run();