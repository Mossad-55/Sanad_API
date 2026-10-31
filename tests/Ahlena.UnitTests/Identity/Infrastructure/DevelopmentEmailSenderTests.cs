using Ahlena.Modules.Identity.Domain.Authentication.VerificationRequests;
using Ahlena.Modules.Identity.Infrastructure.Messaging;

namespace Ahlena.UnitTests.Identity.Infrastructure;

public sealed class DevelopmentEmailSenderTests
{
    [Fact]
    public async Task FamilyInvitation_DoesNotWriteRecipientOrTokenToConsole()
    {
        TextWriter original = Console.Out;
        using StringWriter output = new();
        try
        {
            Console.SetOut(output);
            await new DevelopmentEmailSender().SendFamilyInvitationAsync(
                "user@example.test", "Private family", "ahlena://family/invite?token=secret-token", CancellationToken.None);
        }
        finally
        {
            Console.SetOut(original);
        }

        Assert.DoesNotContain("user@example.test", output.ToString());
        Assert.DoesNotContain("Private family", output.ToString());
        Assert.DoesNotContain("secret-token", output.ToString());
    }

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
