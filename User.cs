using System.Security.Cryptography;
using System.Text;
using MongoDB.Bson.Serialization.Attributes;

namespace NoctesChat;

[BsonIgnoreExtraElements]
public class User
{
    [BsonElement("id"), BsonRequired]
    public ulong ID { get; set; }
    
    [BsonElement("tokens")]
    public List<UserToken> Tokens { get; set; } = [];

    [BsonElement("username"), BsonRequired]
    public string Username { get; set; }

    [BsonElement("email"), BsonRequired]
    public string Email { get; set; }
    
    [BsonElement("email_verified"), BsonRequired]
    public bool EmailVerified { get; set; }
    
    [BsonElement("password_hash")]
    public string PasswordHash { get; set; }
    
    [BsonElement("password_salt")]
    public string PasswordSalt { get; set; }
    
    static HMACSHA256 hmac = new HMACSHA256(Convert.FromHexString(Environment.GetEnvironmentVariable("pwd_pepper")!));

    public static string HashPassword(string password, string salt) {
        var innerHmac = HMACSHA256.HashData(Convert.FromHexString(salt), Encoding.UTF8.GetBytes(password));
        var outerHmac = hmac.ComputeHash(innerHmac);

        return Convert.ToHexString(outerHmac);
    }

    public static (string hash, string salt) HashPassword(string password) {
        var salt = RandomNumberGenerator.GetBytes(16);
        
        var innerHmac = HMACSHA256.HashData(salt, Encoding.UTF8.GetBytes(password));
        var outerHmac = hmac.ComputeHash(innerHmac);

        return (Convert.ToHexString(outerHmac), Convert.ToHexString(salt));
    }
}