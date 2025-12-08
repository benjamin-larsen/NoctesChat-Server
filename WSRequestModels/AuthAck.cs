using System.Text.Json.Serialization;

namespace NoctesChat.WSRequestModels;

public class WSAuthAck(ulong userId) {
    [JsonPropertyName("type")]
    public string Type { get; } = "auth_ack";
    
    [JsonPropertyName("user_id"), JsonNumberHandling(JsonNumberHandling.WriteAsString)]
    public ulong UserId { get; } = userId;
}