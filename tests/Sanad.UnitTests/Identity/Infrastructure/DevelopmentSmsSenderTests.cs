using Sanad.Modules.Identity.Domain.Authentication.VerificationRequests;
using Sanad.Modules.Identity.Infrastructure.Messaging;

namespace Sanad.UnitTests.Identity.Infrastructure;

public sealed class DevelopmentSmsSenderTests
{
    [Fact]
    public async Task SendVerificationCodeAsync_ShouldCompleteWithoutThrowing()
    {
        var sender =
            new DevelopmentSmsSender();

        await sender.SendVerificationCodeAsync(
            "+201001234567",
            "123456",
            VerificationPurpose.VerifyPhone,
            CancellationToken.None);
    }

    [Fact]
    public async Task SendVerificationCodeAsync_ShouldHonorCancellation()
    {
        var sender =
            new DevelopmentSmsSender();

        using var cancellationTokenSource =
            new CancellationTokenSource();

        cancellationTokenSource.Cancel();

        await Assert.ThrowsAnyAsync<
            OperationCanceledException>(
                () => sender.SendVerificationCodeAsync(
                    "+201001234567",
                    "123456",
                    VerificationPurpose.VerifyPhone,
                    cancellationTokenSource.Token));
    }
}