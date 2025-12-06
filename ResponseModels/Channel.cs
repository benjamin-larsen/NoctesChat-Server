using System.Text.Json.Serialization;
using MySqlConnector;

namespace NoctesChat.ResponseModels;

public class ChannelResponse {
    [JsonPropertyName("id"), JsonNumberHandling(JsonNumberHandling.WriteAsString)]
    public required ulong ID { get; set; }
    
    [JsonPropertyName("name")]
    public required string Name { get; set; }
    
    [JsonPropertyName("owner")]
    public required UserResponse Owner { get; set; }
    
    [JsonPropertyName("member_count")]
    public required uint MemberCount { get; set; }
    
    [JsonPropertyName("created_at")]
    public required long CreatedAt { get; set; }
    
    [JsonPropertyName("last_accessed")]
    public required long LastAccessed { get; set; }
    
    public static ChannelResponse FromReader(MySqlDataReader reader, bool includeOwner = true) {
        return new ChannelResponse {
            ID = reader.GetFieldValue<ulong>(0),
            Name = reader.GetFieldValue<string>(2),
            Owner = includeOwner ? UserResponse.FromReader(reader, 5) : UserResponse.Empty,
            MemberCount = reader.GetFieldValue<uint>(3),
            CreatedAt = reader.GetFieldValue<long>(4),
            LastAccessed = reader.GetFieldValue<long>(1)
        };
    }
}
