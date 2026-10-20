using Sanad.Modules.Identity.Domain.Authentication.VerificationRequests;
using Sanad.Modules.Identity.Domain.Support;

namespace Sanad.Modules.Identity.Application.Abstractions.Messaging;

public interface IEmailSender
{
    Task SendEmailAsync(string email, string subject, string body, string? correlationId, CancellationToken cancellationToken);
    Task SendEmailAsync(string email, string subject, string plainTextBody, string? htmlBody, string? correlationId, CancellationToken cancellationToken) =>
        SendEmailAsync(email, subject, plainTextBody, correlationId, cancellationToken);
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

    Task SendSupportReplyAsync(
        string? recipientEmail,
        string subject,
        string replyBody,
        CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
