using ChatApp.Api.Features.Friends.Dtos;
using FluentValidation;

namespace ChatApp.Api.Features.Friends.Validators;

public class SendFriendRequestValidator : AbstractValidator<SendFriendRequest>
{
    public SendFriendRequestValidator()
    {
        RuleFor(x => x.ReceiverId).NotEmpty();
    }
}

public class RenameFriendRequestValidator : AbstractValidator<RenameFriendRequest>
{
    public RenameFriendRequestValidator()
    {
        RuleFor(x => x.SavedName).MaximumLength(100).When(x => x.SavedName != null);
    }
}
