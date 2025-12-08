using System.Text.Json.Serialization;

namespace NoctesChat.WSRequestModels;

public class WSLoginMessage {
    [JsonPropertyName("token"), JsonRequired]
    public required string Token { get; set; }
}