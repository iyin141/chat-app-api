using ChatApp.Api.Features.Stories.Dtos;
using FluentValidation;

namespace ChatApp.Api.Features.Stories.Validators;

public class CreateStoryRequestValidator : AbstractValidator<CreateStoryRequest>
{
    public CreateStoryRequestValidator()
    {
        RuleFor(x => x.MediaUrl).NotEmpty().MaximumLength(2048);
        RuleFor(x => x.Caption).MaximumLength(500).When(x => x.Caption != null);
    }
}
