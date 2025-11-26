using System.Text.Json.Serialization;
using FluentValidation;

namespace NoctesChat.RequestModels;

public class RegisterBody {
    [JsonPropertyName("username")]
    public string Username { get; set; }
    
    [JsonPropertyName("email")]
    public string Email { get; set; }
    
    [JsonPropertyName("password")]
    public string Password { get; set; }
}

public class RegisterValidator : AbstractValidator<RegisterBody>
{
    public RegisterValidator() {
        RuleFor(x => x.Username)
            .NotEmpty().WithMessage("Username is required")
            .Length(3, 20).WithMessage("Username must be between 3 and 20 characters")
            .Matches("^[a-z0-9_]*$").WithMessage("Username may only contain lowercase characters, numbers and underscores.");
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required")
            .EmailAddress().WithMessage("Invalid email address");
        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required");
    }
    
    public static readonly RegisterValidator Instance = new RegisterValidator();
}