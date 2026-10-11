using ChatApp.Api.Features.Profiles.Dtos;
using FluentValidation;

namespace ChatApp.Api.Features.Profiles.Validators;

public class CreateProfileRequestValidator : AbstractValidator<CreateProfileRequest>
{
    public CreateProfileRequestValidator()
    {
        RuleFor(x => x.Username).NotEmpty().MaximumLength(50)
            .Matches("^[a-zA-Z0-9_]+$").WithMessage("Username may only contain letters, numbers and underscores.");
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.AvatarUrl).MaximumLength(2048).When(x => x.AvatarUrl != null);
    }
}

public class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileRequestValidator()
    {
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(100).When(x => x.DisplayName != null);
        RuleFor(x => x.AvatarUrl).MaximumLength(2048).When(x => x.AvatarUrl != null);
    }
}
