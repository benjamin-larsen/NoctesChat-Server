using MongoDB.Bson.Serialization.Attributes;

namespace NoctesChat;

[BsonIgnoreExtraElements]
public class Message
{
    [BsonElement("id"), BsonRequired]
    public UInt64 ID { get; set; }
    
    [BsonElement("channel"), BsonRequired]
    public UInt64 ChannelID { get; set; }

    [BsonElement("author"), BsonRequired]
    public UInt64 Author { get; set; }

    [BsonElement("content"), BsonRequired]
    public string Content { get; set; }
    
    [BsonElement("timestamp"), BsonRequired]
    public Int64 Timestamp { get; set; }
}