using MongoDB.Bson.Serialization.Attributes;

namespace NoctesChat;

[BsonIgnoreExtraElements]
public class Channel
{
    [BsonElement("id"), BsonRequired]
    public UInt64 ID { get; set; }
    
    [BsonElement("members"), BsonRequired]
    public List<UInt64> Members { get; set; }
}