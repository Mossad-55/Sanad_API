using Ahlena.Modules.Identity.Domain.Authentication.VerificationRequests;

namespace Ahlena.Modules.Identity.Application.Abstractions.Messaging;

public interface ISmsSender
{
    Task SendVerificationCodeAsync(
        string phoneNumber,
        string code,
        VerificationPurpose purpose,
        CancellationToken cancellationToken);
}