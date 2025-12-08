using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NoctesChat.WSRequestModels;

public class WSBaseMessage {
    [JsonPropertyName("type"), JsonRequired]
    public required string Type { get; set; }
    
    [JsonPropertyName("data"), JsonRequired]
    public required JsonElement Data { get; set; }

    public void ThrowIfInvalid() {
        if (string.IsNullOrEmpty(Type)) throw new WSException("Invalid type");
        if (Data.ValueKind != JsonValueKind.Object) throw new WSException("Invalid data");
    }
}