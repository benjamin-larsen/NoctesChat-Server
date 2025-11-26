using System.Text.Json.Serialization;
using FluentValidation;

namespace NoctesChat.RequestModels;

public class CreateChannelBody {
    [JsonPropertyName("name")]
    public string Name { get; set; }
    
    [JsonPropertyName("members")]
    public string[] Members { get; set; }
}

public class CreateChannelValidator : AbstractValidator<CreateChannelBody>
{
    public CreateChannelValidator() {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Channel Name is required")
            .Length(3, 20).WithMessage("Channel Name must be between 3 and 20 characters");
        RuleForEach(x => x.Members)
            .Matches("^[1-9][0-9]{15,19}$").WithMessage("Invalid Member ID")
            .NotEmpty().WithMessage("Member ID cannot be empty");
        RuleFor(x => x.Members)
            .Must(ids => ids.Length == ids.Distinct().Count()).WithMessage("You can't specify the same Member ID twice")
            .NotEmpty().WithMessage("You can't create a channel all for yourself");
    }
    
    public static readonly CreateChannelValidator Instance = new CreateChannelValidator();
}