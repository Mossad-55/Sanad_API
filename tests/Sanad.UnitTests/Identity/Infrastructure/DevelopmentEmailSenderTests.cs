using Sanad.Modules.Identity.Domain.Authentication.VerificationRequests;
using Sanad.Modules.Identity.Infrastructure.Messaging;

namespace Sanad.UnitTests.Identity.Infrastructure;

public sealed class DevelopmentEmailSenderTests
{
    [Fact]
    public async Task SendVerificationCodeAsync_ShouldCompleteWithoutThrowing()
    {
        var sender =
            new DevelopmentEmailSender();

        await sender.SendVerificationCodeAsync(
            "user@example.com",
            "123456",
            VerificationPurpose.VerifyEmail,
            CancellationToken.None);
    }

    [Fact]
    public async Task SendVerificationCodeAsync_ShouldHonorCancellation()
    {
        var sender =
            new DevelopmentEmailSender();

        using var cancellationTokenSource =
            new CancellationTokenSource();

        cancellationTokenSource.Cancel();

        await Assert.ThrowsAsync<
            OperationCanceledException>(
            () => sender.SendVerificationCodeAsync(
                "user@example.com",
                "123456",
                VerificationPurpose.VerifyEmail,
                cancellationTokenSource.Token));
    }
}