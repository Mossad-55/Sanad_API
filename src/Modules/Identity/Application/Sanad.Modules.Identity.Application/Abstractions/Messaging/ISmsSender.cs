using Sanad.Modules.Identity.Domain.Authentication.VerificationRequests;

namespace Sanad.Modules.Identity.Application.Abstractions.Messaging;

public interface ISmsSender
{
    Task SendVerificationCodeAsync(
        string phoneNumber,
        string code,
        VerificationPurpose purpose,
        CancellationToken cancellationToken);
}