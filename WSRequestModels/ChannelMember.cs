using System.Text.Json.Serialization;
using NoctesChat.ResponseModels;

namespace NoctesChat.WSRequestModels;

public class WSPushChannelMember {
    [JsonPropertyName("type")]
    public string Type { get; } = "push_channel_member";
    
    [JsonPropertyName("channel"), JsonNumberHandling(JsonNumberHandling.WriteAsString)]
    public ulong Channel { get; set; }
    
    [JsonPropertyName("member")]
    public UserResponse Member { get; set; }
}

public class WSDeleteChannelMember {
    [JsonPropertyName("type")]
    public string Type { get; } = "delete_channel_member";

    [JsonPropertyName("channel"), JsonNumberHandling(JsonNumberHandling.WriteAsString)]
    public ulong Channel { get; set; }
    
    [JsonPropertyName("member"), JsonNumberHandling(JsonNumberHandling.WriteAsString)]
    public ulong Member { get; set; }
}