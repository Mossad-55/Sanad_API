using Sanad.Modules.Identity.Application.Abstractions.Messaging;
using Sanad.Modules.Identity.Domain.Authentication.VerificationRequests;
using Sanad.Modules.Identity.Domain.Support;

namespace Sanad.Modules.Identity.Infrastructure.Messaging;

public sealed class DevelopmentEmailSender :
    IEmailSender
{
    public Task SendEmailAsync(string email, string subject, string body, string? correlationId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Console.WriteLine($"[DevEmail] Message to {email}: {subject}");
        return Task.CompletedTask;
    }
    public Task SendEmailAsync(string email, string subject, string plainTextBody, string? htmlBody, string? correlationId, CancellationToken cancellationToken) =>
        SendEmailAsync(email, subject, plainTextBody, correlationId, cancellationToken);
    public Task SendFamilyInvitationAsync(string email, string familyName, string inviteLink, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Console.WriteLine("[DevEmail] Family invitation suppressed in Development.");

        return Task.CompletedTask;
    }

    public Task SendVerificationCodeAsync(
        string email,
        string code,
        VerificationPurpose purpose,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.CompletedTask;
    }

    public Task SendSupportRequestAsync(
        string senderName,
        string? senderEmail,
        string senderPhoneNumber,
        string accountTypes,
        string subject,
        string message,
        CancellationToken cancellationToken)
    {
        Console.WriteLine(
            $"[DevEmail] Support request from {senderName} ({senderEmail}) — {subject}");

        return Task.CompletedTask;
    }
}
