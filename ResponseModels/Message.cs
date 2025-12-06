using System.Text.Json.Serialization;
using MySqlConnector;

namespace NoctesChat.ResponseModels;

public class MessageResponse {
    [JsonPropertyName("id"), JsonNumberHandling(JsonNumberHandling.WriteAsString)]
    public required ulong ID { get; set; }
    
    [JsonPropertyName("author")]
    public UserResponse? Author { get; set; }
    
    [JsonPropertyName("content")]
    public required string Content { get; set; }
    
    [JsonPropertyName("timestamp")]
    public required long Timestamp { get; set; }
    
    [JsonPropertyName("edited")]
    public long? EditedTimestamp { get; set; }
    
    public static MessageResponse FromReader(MySqlDataReader reader) {
        var hasAuthor = !reader.IsDBNull(4 /* Author ID */);
        var hasEdited = !reader.IsDBNull(3 /* Message Edited Timestamp */);

        return new MessageResponse {
            ID = reader.GetFieldValue<ulong>(0),
            Author = hasAuthor ? UserResponse.FromReader(reader, 4) : null,
            Content = reader.GetFieldValue<string>(1),
            Timestamp = reader.GetFieldValue<long>(2),
            EditedTimestamp = hasEdited ? reader.GetFieldValue<long>(3) : null
        };
    }
}
