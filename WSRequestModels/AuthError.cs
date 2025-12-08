using System.Text.Json.Serialization;

namespace NoctesChat.WSRequestModels;

public class WSAuthError(string message, int errorCode) {
    [JsonPropertyName("type")]
    public string Type { get; } = "auth_error";

    [JsonPropertyName("error")]
    public string Message { get; } = message;
    
    [JsonPropertyName("code")]
    public int ErrorCode { get; } = errorCode;
}