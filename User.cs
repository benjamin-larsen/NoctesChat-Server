using System.Security.Cryptography;
using System.Text;

namespace NoctesChat;

public class User
{
    public ulong ID { get; set; }
    
    public List<UserToken> Tokens { get; set; } = [];

    public string Username { get; set; }

    public string? Email { get; set; }
    
    public bool EmailVerified { get; set; }
    
    public byte[]? PasswordHash { get; set; }
    
    public byte[]? PasswordSalt { get; set; }
    
    public long CreatedAt { get; set; }
    
    static byte[] pepper = Convert.FromHexString(Environment.GetEnvironmentVariable("pwd_pepper")!);
    
    public static byte[] HashPassword(string password, byte[] salt) {
        var innerHmac = HMACSHA256.HashData(salt, Encoding.UTF8.GetBytes(password));
        var outerHmac = HMACSHA256.HashData(pepper, innerHmac);

        return outerHmac;
    }

    public static (byte[] hash, byte[] salt) HashPassword(string password) {
        var salt = RandomNumberGenerator.GetBytes(16);
        
        var innerHmac = HMACSHA256.HashData(salt, Encoding.UTF8.GetBytes(password));
        var outerHmac = HMACSHA256.HashData(pepper, innerHmac);

        return (outerHmac, salt);
    }
}