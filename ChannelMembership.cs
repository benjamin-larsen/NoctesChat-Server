using MongoDB.Bson.Serialization.Attributes;

namespace NoctesChat;

[BsonIgnoreExtraElements]
public class ChannelMembership {
    [BsonElement("user"), BsonRequired]
    public ulong UserID { get; set; }
    
    [BsonElement("channel"), BsonRequired]
    public ulong ChannelID { get; set; }
    
    [BsonElement("last_accessed"), BsonRequired]
    public long LastAccessed { get; set; }
}