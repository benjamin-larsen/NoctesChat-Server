using System.Text.Json.Serialization;
using NoctesChat.ResponseModels;

namespace NoctesChat.WSRequestModels;

public class WSPushChannel {
    [JsonPropertyName("type")]
    public string Type { get; } = "push_channel";
    
    [JsonPropertyName("channel")]
    public required ChannelResponse Channel { get; set; }
    
    [JsonPropertyName("members")]
    public required List<UserResponse> Members { get; set; }
}

public class WSUpdateChannel {
    [JsonPropertyName("type")]
    public string Type { get; } = "update_channel";
    
    [JsonPropertyName("channel")]
    public required ChannelResponse Channel { get; set; }
}

public class WSDeleteChannel {
    [JsonPropertyName("type")]
    public string Type { get; } = "delete_channel";

    [JsonPropertyName("channel"), JsonNumberHandling(JsonNumberHandling.WriteAsString)]
    public ulong Channel { get; set; }
}