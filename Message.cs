using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace NoctesChat;

[BsonIgnoreExtraElements]
public class Message
{
    [BsonElement("id"), BsonRequired, BsonSerializer(typeof(BinU64DBSerializer))]
    public ulong ID { get; set; }
    
    [BsonElement("channel"), BsonRequired]
    public ulong ChannelID { get; set; }

    [BsonElement("author"), BsonRequired]
    public ulong Author { get; set; }

    [BsonElement("content"), BsonRequired]
    public string Content { get; set; }
    
    [BsonElement("timestamp"), BsonRequired]
    public long Timestamp { get; set; }
    
    [BsonElement("edited")]
    public long? EditedTimestamp { get; set; }
}