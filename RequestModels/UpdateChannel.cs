using System.Text.Json.Serialization;
using FluentValidation;

namespace NoctesChat.RequestModels;

public class UpdateChannelBody {
    [JsonPropertyName("owner")]
    public ulong? Owner { get; set; }
    
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public class UpdateChannelValidator : AbstractValidator<UpdateChannelBody>
{
    public UpdateChannelValidator() {
        RuleFor(x => x)
            .Must(x => x.Owner != null || x.Name != null).WithMessage("You need to update at least Owner or Channel Name");
        RuleFor(x=>x.Name)
            .Length(3, 50).WithMessage("Channel Name must be between 3 and 50 characters");
    }
    
    public static readonly UpdateChannelValidator Instance = new UpdateChannelValidator();
}