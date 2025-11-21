using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;

namespace NoctesChat;

[BsonIgnoreExtraElements]
public class User
{
    [BsonElement("name")]
    [JsonPropertyName("name")]
    public string Name { get; set; }

    [BsonElement("password")]
    [JsonPropertyName("password")]
    public string Password { get; set; }
    
}