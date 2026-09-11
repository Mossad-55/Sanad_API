using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Application.Abstractions.Messaging;
using Sanad.Modules.Identity.Application.Support;
using Sanad.Modules.Identity.Application.Users;
using Sanad.Modules.Identity.Domain.Authentication.VerificationRequests;
using Sanad.Modules.Identity.Domain.Support;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.UnitTests.Identity.Registration;

namespace Sanad.UnitTests.Identity.Support;

public sealed class SubmitSupportRequestCommandHandlerTests
{
    [Fact]
    public async Task Handle_ShouldPersistTicketAndSendEmail()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        RecordingEmailSender emailSender =
            new();

        SubmitSupportRequestCommandHandler handler =
            CreateHandler(
                dbContext,
                emailSender);

        User user =
            await SeedUserAsync(dbContext);

        dbContext.ResetSaveChangesCalls();

        SubmitSupportRequestCommand command =
            new(
                user.Id,
                "Help needed",
                "I need assistance with my account.");

        Result<SupportRequestResponse> result =
            await handler.Handle(
                command,
                CancellationToken.None);

        Assert.True(result.IsSuccess);

        Assert.Equal(
            SupportTicketStatus.New,
            result.Value.Status);

        Assert.Equal(
            2,
            dbContext.SaveChangesCalls);

        SupportTicket persistedTicket =
            await dbContext.SupportTickets
                .SingleAsync();

        Assert.Equal(
            result.Value.TicketId,
            persistedTicket.Id);

        Assert.Single(emailSender.SupportRequests);

        var supportRequest = emailSender.SupportRequests[0];
        Assert.Equal(
            user.ArabicFullName.Value,
            supportRequest.SenderName);
        Assert.Equal("Help needed", supportRequest.Subject);
        Assert.Equal(
            "I need assistance with my account.",
            supportRequest.Message);
    }

    [Fact]
    public async Task Handle_ShouldReturnUserNotFound_WhenUserUnknown()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        RecordingEmailSender emailSender =
            new();

        SubmitSupportRequestCommandHandler handler =
            CreateHandler(
                dbContext,
                emailSender);

        dbContext.ResetSaveChangesCalls();

        SubmitSupportRequestCommand command =
            new(
                UserId.New(),
                "Help needed",
                "I need assistance with my account.");

        Result<SupportRequestResponse> result =
            await handler.Handle(
                command,
                CancellationToken.None);

        Assert.True(result.IsFailure);

        Assert.Equal(
            AccountErrors.UserNotFound,
            result.Error);

        Assert.Equal(
            0,
            dbContext.SaveChangesCalls);

        Assert.Empty(emailSender.SupportRequests);
    }

    [Fact]
    public async Task Handle_ShouldReturnInvalidOperation_WhenUserBlocked()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        RecordingEmailSender emailSender =
            new();

        SubmitSupportRequestCommandHandler handler =
            CreateHandler(
                dbContext,
                emailSender);

        User user =
            await SeedUserAsync(dbContext);

        user.Block(
            "Security block.",
            CreateUtcDateTime());

        await dbContext.SaveChangesAsync();

        dbContext.ResetSaveChangesCalls();

        SubmitSupportRequestCommand command =
            new(
                user.Id,
                "Help needed",
                "I need assistance with my account.");

        Result<SupportRequestResponse> result =
            await handler.Handle(
                command,
                CancellationToken.None);

        Assert.True(result.IsFailure);

        Assert.Equal(
            AccountErrors.InvalidOperation,
            result.Error);

        Assert.Equal(
            0,
            dbContext.SaveChangesCalls);

        Assert.Empty(emailSender.SupportRequests);
    }

    private static IdentityTestDbContext CreateDbContext()
    {
        DbContextOptions<IdentityTestDbContext>
            options =
                new DbContextOptionsBuilder<
                    IdentityTestDbContext>()
                    .UseInMemoryDatabase(
                        Guid.NewGuid()
                            .ToString())
                    .Options;

        return new IdentityTestDbContext(
            options);
    }

    private static async Task<User> SeedUserAsync(
        IdentityTestDbContext dbContext)
    {
        User user =
            User.Create(
                FullName.Create(
                    "محمد أحمد"),
                FullName.Create(
                    "Mohamed Ahmed"),
                Email.Create(
                    "mohamed@example.com"),
                PhoneNumber.Create(
                    "+201001234567"));

        dbContext.Users.Add(user);

        await dbContext.SaveChangesAsync();

        dbContext.ResetSaveChangesCalls();

        return user;
    }

    private static DateTime CreateUtcDateTime()
    {
        return new DateTime(
            2026,
            8,
            20,
            10,
            0,
            0,
            DateTimeKind.Utc);
    }

    private static SubmitSupportRequestCommandHandler CreateHandler(
        IdentityTestDbContext dbContext,
        RecordingEmailSender emailSender)
    {
        return new SubmitSupportRequestCommandHandler(
            dbContext,
            emailSender,
            new FixedDateTimeProvider());
    }

    private sealed class FixedDateTimeProvider :
        IDateTimeProvider
    {
        internal static readonly DateTime
            UtcNowValue =
                new(
                    2026,
                    8,
                    20,
                    10,
                    0,
                    0,
                    DateTimeKind.Utc);

        public DateTime UtcNow =>
            UtcNowValue;
    }

    private sealed class RecordingEmailSender :
        IEmailSender
    {
        internal List<SupportRequestMessage>
            SupportRequests { get; } = [];

        public Task SendSupportRequestAsync(
            string senderName,
            string? senderEmail,
            string senderPhoneNumber,
            string accountTypes,
            string subject,
            string message,
            CancellationToken cancellationToken)
        {
            SupportRequests.Add(
                new SupportRequestMessage(
                    senderName,
                    senderEmail,
                    senderPhoneNumber,
                    accountTypes,
                    subject,
                    message));

            return Task.CompletedTask;
        }

        public Task SendVerificationCodeAsync(
            string email,
            string code,
            VerificationPurpose purpose,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task SendFamilyInvitationAsync(
            string email,
            string familyName,
            string inviteLink,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        internal sealed record SupportRequestMessage(
            string SenderName,
            string? SenderEmail,
            string SenderPhoneNumber,
            string AccountTypes,
            string Subject,
            string Message);
    }
}
