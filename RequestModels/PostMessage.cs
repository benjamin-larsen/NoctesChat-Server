using System.Text.Json.Serialization;
using FluentValidation;

namespace NoctesChat.RequestModels;

public class PostMessageBody {
    [JsonPropertyName("content")]
    public required string Content { get; set; }
}

public class PostMessageValidator : AbstractValidator<PostMessageBody>
{
    public PostMessageValidator() {
        RuleFor(x => x.Content)
            .NotEmpty().WithMessage("Content is required")
            .MaximumLength(2000).WithMessage("Content must not be more than 2000 characters");
    }
    
    public static readonly PostMessageValidator Instance = new PostMessageValidator();
}
