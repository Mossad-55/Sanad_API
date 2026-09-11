using Sanad.Modules.Identity.Domain.Authentication.VerificationRequests;
using Sanad.Modules.Identity.Domain.Support;

namespace Sanad.Modules.Identity.Application.Abstractions.Messaging;

public interface IEmailSender
{
    Task SendVerificationCodeAsync(
        string email,
        string code,
        VerificationPurpose purpose,
        CancellationToken cancellationToken);

    Task SendFamilyInvitationAsync(
        string email,
        string familyName,
        string inviteLink,
        CancellationToken cancellationToken);

    Task SendSupportRequestAsync(
        string senderName,
        string? senderEmail,
        string senderPhoneNumber,
        string accountTypes,
        string subject,
        string message,
        CancellationToken cancellationToken);
}
