using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using SharpMongoRepository;

namespace API.Models;

/// <summary>
///     A stored weather reading.
/// </summary>
/// <remarks>
///     A class rather than a positional record: the repository writes a generated key back onto
///     <see cref="Id" /> during insert, which needs a settable property.
/// </remarks>
[BsonCollection("forecasts")]
public sealed class WeatherForecast : IDocument<Guid>
{
    /// <summary>Gets or sets the primary key, stored as a string so it is readable in the shell.</summary>
    [BsonRepresentation(BsonType.String)]
    public Guid Id { get; set; }

    /// <summary>Gets or sets the date the reading is for.</summary>
    public DateOnly Date { get; set; }

    /// <summary>Gets or sets the temperature in degrees Celsius.</summary>
    public int TemperatureC { get; set; }

    /// <summary>Gets or sets a short description of the conditions.</summary>
    public string? Summary { get; set; }

    /// <summary>Gets or sets the city the reading was taken in.</summary>
    public string City { get; set; } = string.Empty;

    /// <summary>Gets the temperature in degrees Fahrenheit.</summary>
    [BsonIgnore]
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

/// <summary>Request body for creating or updating a forecast.</summary>
/// <param name="Date">Date the reading is for.</param>
/// <param name="TemperatureC">Temperature in degrees Celsius.</param>
/// <param name="Summary">Short description of the conditions.</param>
/// <param name="City">City the reading was taken in.</param>
public sealed record ForecastRequest(DateOnly Date, int TemperatureC, string? Summary, string City)
{
    /// <summary>Builds a document from this request.</summary>
    public WeatherForecast ToDocument() => new()
    {
        Date = Date,
        TemperatureC = TemperatureC,
        Summary = Summary,
        City = City
    };
}
