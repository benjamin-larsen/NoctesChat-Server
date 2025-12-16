using System.Text.Json.Serialization;

namespace NoctesChat.WSRequestModels;

public class WSHeartbeatAck {
    [JsonPropertyName("type")]
    public string Type { get; } = "heartbeat_ack";
}