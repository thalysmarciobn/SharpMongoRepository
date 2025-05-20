using MongoDB.Bson;
using SharpMongoRepository.Attributes;
using SharpMongoRepository.Interface;

namespace API;

[BsonCollection("temperature")]
public record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary) : IDocument
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
    public ObjectId Id { get; set; }
}