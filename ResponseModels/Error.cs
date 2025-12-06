using System.Text.Json.Serialization;

namespace NoctesChat.ResponseModels;

public class ErrorResponse(string message) {
    [JsonPropertyName("error")]
    public string Message { get; set; } = message;
}
