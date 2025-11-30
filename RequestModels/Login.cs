using System.Text.Json.Serialization;
using FluentValidation;

namespace NoctesChat.RequestModels;

public class LoginBody {
    [JsonPropertyName("email")]
    public string Email { get; set; }
    
    [JsonPropertyName("password")]
    public string Password { get; set; }
}

public class LoginValidator : AbstractValidator<LoginBody>
{
    public LoginValidator() {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required")
            .EmailAddress().WithMessage("Invalid email address")
            .MaximumLength(254).WithMessage("Email Address is too long: can't be more than 254 characters");
        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required");
    }
    
    public static readonly LoginValidator Instance = new LoginValidator();
}