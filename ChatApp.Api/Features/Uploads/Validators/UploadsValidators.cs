using ChatApp.Api.Features.Uploads.Dtos;
using FluentValidation;

namespace ChatApp.Api.Features.Uploads.Validators;

public class PresignUploadRequestValidator : AbstractValidator<PresignUploadRequest>
{
    public PresignUploadRequestValidator()
    {
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(255);
        RuleFor(x => x.ContentType).MaximumLength(100).When(x => x.ContentType != null);
    }
}
