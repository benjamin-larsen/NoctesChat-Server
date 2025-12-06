namespace NoctesChat.DataModels;

public class UserLoginData {
    public required ulong ID { get; set; }
    
    public byte[]? PasswordHash { get; set; }
    
    public byte[]? PasswordSalt { get; set; }
}
