using Ahlena.Modules.Identity.Application.Abstractions.Messaging;
using Ahlena.Modules.Identity.Domain.Authentication.VerificationRequests;

namespace Ahlena.Modules.Identity.Infrastructure.Messaging;

public sealed class DevelopmentSmsSender :
    ISmsSender
{
    public Task SendVerificationCodeAsync(
        string phoneNumber,
        string code,
        VerificationPurpose purpose,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.CompletedTask;
    }
}