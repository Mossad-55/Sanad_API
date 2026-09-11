using Sanad.Modules.Identity.Application.Abstractions.Messaging;
using Sanad.Modules.Identity.Domain.Authentication.VerificationRequests;
using Sanad.Modules.Identity.Domain.Support;

namespace Sanad.Modules.Identity.Infrastructure.Messaging;

public sealed class DevelopmentEmailSender :
    IEmailSender
{
    public Task SendFamilyInvitationAsync(string email, string familyName, string inviteLink, CancellationToken cancellationToken)
    {
        Console.WriteLine(
            $"[DevEmail] Family invitation to {email} " +
            $"for '{familyName}': {inviteLink}");

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