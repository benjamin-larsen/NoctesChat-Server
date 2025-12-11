using System.Text.Json.Serialization;

namespace NoctesChat.WSRequestModels;

public class WSError(string message, string respondType, int errorCode) {
    [JsonPropertyName("type")]
    public string Type { get; } = "error";
    
    [JsonPropertyName("respond_type")]
    public string RespondType { get; } = respondType;

    [JsonPropertyName("error")]
    public string Message { get; } = message;
    
    [JsonPropertyName("code")]
    public int ErrorCode { get; } = errorCode;
}