using Sanad.Modules.Identity.Domain.Authentication.VerificationRequests;

namespace Sanad.Modules.Identity.Application.Abstractions.Messaging;

public interface IEmailSender
{
    Task SendVerificationCodeAsync(
        string email,
        string code,
        VerificationPurpose purpose,
        CancellationToken cancellationToken);
}