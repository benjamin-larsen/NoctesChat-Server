using System.Text.Json.Serialization;
using MySqlConnector;

namespace NoctesChat.ResponseModels;

public class UserResponse {
    [JsonPropertyName("id"), JsonPropertyOrder(1), JsonNumberHandling(JsonNumberHandling.WriteAsString)]
    public required ulong ID { get; set; }
    
    [JsonPropertyName("username"), JsonPropertyOrder(2)]
    public required string Username { get; set; }
    
    [JsonPropertyName("created_at"), JsonPropertyOrder(5)]
    public required long CreatedAt { get; set; }

    public static UserResponse FromReader(MySqlDataReader reader, int offset = 0) {
        return new UserResponse {
            ID = reader.GetUInt64(offset++),
            Username = reader.GetString(offset++),
            CreatedAt = reader.GetInt64(offset),
        };
    }

    public static readonly UserResponse Empty = new UserResponse {
        ID = 0,
        Username = "",
        CreatedAt = 0,
    };
}

public class AuthenticatedUserResponse : UserResponse {
    [JsonPropertyName("email"), JsonPropertyOrder(3)]
    public required string Email { get; set; }
    
    [JsonPropertyName("email_verified"), JsonPropertyOrder(4)]
    public required bool EmailVerified { get; set; }
}
