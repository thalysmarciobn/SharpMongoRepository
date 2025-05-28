using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using SharpMongoRepository.Attributes;
using SharpMongoRepository.Interface;

namespace API;

[BsonCollection("temperature")]
public record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary) : IDocument<Guid>
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);

    [BsonRepresentation(BsonType.String)]
    public Guid Id { get; set; }
}