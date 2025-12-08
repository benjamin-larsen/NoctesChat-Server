using System.Text.Json.Serialization;
using NoctesChat.ResponseModels;

namespace NoctesChat.WSRequestModels;

public class WSPushMessage {
    [JsonPropertyName("type")]
    public string Type { get; } = "push_message";
    
    [JsonPropertyName("message")]
    public MessageResponse Message { get; set; }
}