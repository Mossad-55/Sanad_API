using Sanad.Modules.Identity.Application.Abstractions.Messaging;
using Sanad.Modules.Identity.Domain.Authentication.VerificationRequests;

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
}