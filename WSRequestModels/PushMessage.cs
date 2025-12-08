using System.Text.Json.Serialization;
using NoctesChat.ResponseModels;

namespace NoctesChat.WSRequestModels;

public class WSPushMessage {
    [JsonPropertyName("type")]
    public string Type { get; } = "push_message";
    
    [JsonPropertyName("channel"), JsonNumberHandling(JsonNumberHandling.WriteAsString)]
    public ulong Channel { get; set; }
    
    [JsonPropertyName("message")]
    public required MessageResponse Message { get; set; }
}