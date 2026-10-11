using ChatApp.Api.Features.Messaging.Dtos;
using FluentValidation;

namespace ChatApp.Api.Features.Messaging.Validators;

public class SendMessageRequestValidator : AbstractValidator<SendMessageRequest>
{
    public SendMessageRequestValidator()
    {
        RuleFor(x => x.Content).NotEmpty().MaximumLength(4000);
        RuleFor(x => x.TempId).MaximumLength(64).When(x => x.TempId != null);
        RuleFor(x => x.ReplyToId).GreaterThan(0).When(x => x.ReplyToId.HasValue);
    }
}

public class UpdateMessageRequestValidator : AbstractValidator<UpdateMessageRequest>
{
    public UpdateMessageRequestValidator()
    {
        RuleFor(x => x.Content).NotEmpty().MaximumLength(4000);
    }
}
