using MongoDB.Bson.Serialization.Attributes;

namespace NoctesChat;

[BsonIgnoreExtraElements]
public class Channel
{
    [BsonElement("id"), BsonRequired]
    public ulong ID { get; set; }
    
    [BsonElement("owner"), BsonRequired]
    public ulong OwnerID { get; set; }
    
    [BsonElement("name"), BsonRequired]
    public string ChannelName { get; set; }
}