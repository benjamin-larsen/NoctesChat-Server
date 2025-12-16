using System.Text.Json.Serialization;

namespace NoctesChat.WSRequestModels;

public class WSPushPresence {
    [JsonPropertyName("type")]
    public string Type { get; } = "push_presence";
    
    [JsonPropertyName("user"), JsonNumberHandling(JsonNumberHandling.WriteAsString)]
    public ulong User { get; set; }
    
    [JsonPropertyName("status")]
    public string Status { get; set; }
}