using System.Text.Json.Serialization;
using MySqlConnector;
using NoctesChat.ResponseModels;

namespace NoctesChat.WSRequestModels;

public class WSAuthChannel {
    [JsonPropertyName("id"), JsonNumberHandling(JsonNumberHandling.WriteAsString)]
    public required ulong ID { get; set; }
    
    [JsonPropertyName("name")]
    public required string Name { get; set; }
    
    [JsonPropertyName("owner")]
    public required UserResponse Owner { get; set; }

    [JsonPropertyName("members")]
    public List<UserResponse> Members { get; set; } = [];
    
    [JsonPropertyName("created_at")]
    public required long CreatedAt { get; set; }
    
    [JsonPropertyName("last_accessed")]
    public required long LastAccessed { get; set; }
    
    public static WSAuthChannel FromReader(MySqlDataReader reader) {
        return new WSAuthChannel {
            ID = reader.GetFieldValue<ulong>(0),
            Name = reader.GetFieldValue<string>(2),
            Owner = UserResponse.FromReader(reader, 4),
            CreatedAt = reader.GetFieldValue<long>(3),
            LastAccessed = reader.GetFieldValue<long>(1)
        };
    }
}

public class WSAuthAck(ulong userId, List<WSAuthChannel> channels) {
    [JsonPropertyName("type")]
    public string Type { get; } = "auth_ack";
    
    [JsonPropertyName("user_id"), JsonNumberHandling(JsonNumberHandling.WriteAsString)]
    public ulong UserId { get; } = userId;

    [JsonPropertyName("channels")]
    public List<WSAuthChannel> Channels { get; set; } = channels;
}