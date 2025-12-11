using System.Text.Json.Serialization;

namespace NoctesChat.WSRequestModels;

public class WSTyping {
    [JsonPropertyName("channel")]
    public required ulong Channel { get; set; }

    public enum Variant {
        Start,
        Stop
    };
}

public class WSAnnounceStartTyping {
    [JsonPropertyName("type")]
    public string Type { get; } = "start_typing";
    
    [JsonPropertyName("member"), JsonNumberHandling(JsonNumberHandling.WriteAsString)]
    public ulong Member { get; set; }
    
    [JsonPropertyName("channel"), JsonNumberHandling(JsonNumberHandling.WriteAsString)]
    public ulong Channel { get; set; }
}

public class WSAnnounceStopTyping {
    [JsonPropertyName("type")]
    public string Type { get; } = "stop_typing";
    
    [JsonPropertyName("member"), JsonNumberHandling(JsonNumberHandling.WriteAsString)]
    public ulong Member { get; set; }
    
    [JsonPropertyName("channel"), JsonNumberHandling(JsonNumberHandling.WriteAsString)]
    public ulong Channel { get; set; }
}