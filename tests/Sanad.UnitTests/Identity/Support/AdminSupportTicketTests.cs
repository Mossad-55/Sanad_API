using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Application.Abstractions.Messaging;
using Sanad.Modules.Identity.Application.Support;
using Sanad.Modules.Identity.Domain.Authentication.VerificationRequests;
using Sanad.Modules.Identity.Domain.Support;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.UnitTests.Identity.Registration;

namespace Sanad.UnitTests.Identity.Support;

/// <summary>
/// F5 admin ticket workflow. Pins the approved numeric status contract, the
/// forward-only transition matrix (nothing leaves Closed, no reopen, no
/// no-op), durable replies carrying actor and time, and the rule that a saved
/// reply stays readable when email and in-app delivery both fail.
/// </summary>
public sealed class AdminSupportTicketTests
{
    private static readonly DateTime Now =
        new(2026, 10, 20, 10, 0, 0, DateTimeKind.Utc);

    private static readonly UserId AdminId = UserId.New();

    public static TheoryData<SupportTicketStatus, SupportTicketStatus>
        AllowedTransitions => new()
        {
            { SupportTicketStatus.New, SupportTicketStatus.InProgress },
            { SupportTicketStatus.New, SupportTicketStatus.Resolved },
            { SupportTicketStatus.New, SupportTicketStatus.Closed },
            { SupportTicketStatus.InProgress, SupportTicketStatus.Resolved },
            { SupportTicketStatus.InProgress, SupportTicketStatus.Closed },
            { SupportTicketStatus.Resolved, SupportTicketStatus.Closed }
        };

    public static TheoryData<SupportTicketStatus, SupportTicketStatus>
        RefusedTransitions => new()
        {
            { SupportTicketStatus.New, SupportTicketStatus.New },
            { SupportTicketStatus.InProgress, SupportTicketStatus.New },
            { SupportTicketStatus.InProgress, SupportTicketStatus.InProgress },
            { SupportTicketStatus.Resolved, SupportTicketStatus.New },
            { SupportTicketStatus.Resolved, SupportTicketStatus.InProgress },
            { SupportTicketStatus.Resolved, SupportTicketStatus.Resolved },
            { SupportTicketStatus.Closed, SupportTicketStatus.New },
            { SupportTicketStatus.Closed, SupportTicketStatus.InProgress },
            { SupportTicketStatus.Closed, SupportTicketStatus.Resolved },
            { SupportTicketStatus.Closed, SupportTicketStatus.Closed }
        };

    [Fact]
    public void StatusEnum_PinsTheApprovedNumericContract()
    {
        Assert.Equal(1, (int)SupportTicketStatus.New);
        Assert.Equal(2, (int)SupportTicketStatus.InProgress);
        Assert.Equal(3, (int)SupportTicketStatus.Resolved);
        Assert.Equal(4, (int)SupportTicketStatus.Closed);
    }

    [Fact]
    public void TransitionMatrix_IsForwardOnly_NeverLeavesClosed_AndMatchesThePinnedSet()
    {
        List<string> allowed = [];

        foreach (SupportTicketStatus from in
            Enum.GetValues<SupportTicketStatus>())
        {
            foreach (SupportTicketStatus to in
                Enum.GetValues<SupportTicketStatus>())
            {
                bool isAllowed =
                    SupportTicketTransitions.IsAllowed(from, to);

                if (isAllowed)
                {
                    allowed.Add($"{from}->{to}");

                    Assert.True(
                        (int)to > (int)from,
                        $"{from}->{to} must move forward only.");
                }

                if (from == SupportTicketStatus.Closed)
                {
                    Assert.False(
                        isAllowed,
                        $"Closed is terminal: {from}->{to} is not allowed.");
                }

                if (from == to)
                {
                    Assert.False(
                        isAllowed,
                        $"A no-op is not a transition: {from}.");
                }
            }
        }

        Assert.Equal(
            new[]
            {
                "New->InProgress",
                "New->Resolved",
                "New->Closed",
                "InProgress->Resolved",
                "InProgress->Closed",
                "Resolved->Closed"
            },
            allowed);
    }

    [Theory]
    [MemberData(nameof(AllowedTransitions))]
    public async Task ChangeStatus_AllowsEveryPinnedTransition_AndRecordsActorAndTime(
        SupportTicketStatus from,
        SupportTicketStatus to)
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        SupportTicket ticket =
            await SeedTicketAsync(dbContext, from);

        ChangeSupportTicketStatusCommandHandler handler =
            new(dbContext, new FixedDateTimeProvider());

        Result<bool> result =
            await handler.Handle(
                new ChangeSupportTicketStatusCommand(
                    ticket.Id,
                    to,
                    AdminId),
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(to, ticket.Status);
        Assert.Equal(AdminId, ticket.StatusChangedByUserId!.Value);
        Assert.Equal(Now, ticket.StatusChangedOnUtc);
        Assert.Equal(1, dbContext.SaveChangesCalls);
    }

    [Theory]
    [MemberData(nameof(RefusedTransitions))]
    public async Task ChangeStatus_RefusesDisallowedTransitions_AndWritesNothing(
        SupportTicketStatus from,
        SupportTicketStatus to)
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        SupportTicket ticket =
            await SeedTicketAsync(dbContext, from);

        ChangeSupportTicketStatusCommandHandler handler =
            new(dbContext, new FixedDateTimeProvider());

        Result<bool> result =
            await handler.Handle(
                new ChangeSupportTicketStatusCommand(
                    ticket.Id,
                    to,
                    AdminId),
                CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(SupportTicketErrors.Conflict, result.Error);
        Assert.Equal(from, ticket.Status);
        Assert.Equal(0, dbContext.SaveChangesCalls);
    }

    [Fact]
    public async Task ChangeStatus_ReturnsNotFound_WhenTheTicketIsUnknown()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        ChangeSupportTicketStatusCommandHandler handler =
            new(dbContext, new FixedDateTimeProvider());

        Result<bool> result =
            await handler.Handle(
                new ChangeSupportTicketStatusCommand(
                    new SupportTicketId(Guid.NewGuid()),
                    SupportTicketStatus.InProgress,
                    AdminId),
                CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(SupportTicketErrors.NotFound, result.Error);
        Assert.Equal(0, dbContext.SaveChangesCalls);
    }

    [Theory]
    [InlineData(SupportTicketStatus.New)]
    [InlineData(SupportTicketStatus.InProgress)]
    [InlineData(SupportTicketStatus.Resolved)]
    public async Task Reply_PersistsDurableReplyWithActorAndTime_OnEveryOpenStatus(
        SupportTicketStatus status)
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        SupportTicket ticket =
            await SeedTicketAsync(dbContext, status);

        FakeEmailSender emailSender = new();
        FakeNotifier notifier = new();

        ReplyToSupportTicketCommandHandler handler =
            new(
                dbContext,
                new FixedDateTimeProvider(),
                emailSender,
                notifier);

        Result<SupportTicketReplyDto> result =
            await handler.Handle(
                new ReplyToSupportTicketCommand(
                    ticket.Id,
                    AdminId,
                    "  We are on it.  "),
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("We are on it.", result.Value.Body);
        Assert.Equal(AdminId.Value, result.Value.ActorUserId);
        Assert.Equal(Now, result.Value.CreatedOnUtc);

        SupportTicketReply persisted =
            await dbContext.SupportTicketReplies.SingleAsync();

        Assert.Equal(ticket.Id, persisted.TicketId);
        Assert.Equal(AdminId, persisted.ActorUserId);
        Assert.Equal(1, dbContext.SaveChangesCalls);

        // An admin reply never changes the status.
        Assert.Equal(status, ticket.Status);

        Assert.Equal(1, emailSender.ReplyAttempts);
        Assert.Equal(1, notifier.Attempts);
    }

    [Fact]
    public async Task Reply_OnClosedTicket_ReturnsConflict_AndWritesNothing()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        SupportTicket ticket =
            await SeedTicketAsync(
                dbContext,
                SupportTicketStatus.Closed);

        FakeEmailSender emailSender = new();
        FakeNotifier notifier = new();

        ReplyToSupportTicketCommandHandler handler =
            new(
                dbContext,
                new FixedDateTimeProvider(),
                emailSender,
                notifier);

        Result<SupportTicketReplyDto> result =
            await handler.Handle(
                new ReplyToSupportTicketCommand(
                    ticket.Id,
                    AdminId,
                    "Too late."),
                CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(SupportTicketErrors.Conflict, result.Error);
        Assert.Equal(
            0,
            await dbContext.SupportTicketReplies.CountAsync());
        Assert.Equal(0, dbContext.SaveChangesCalls);
        Assert.Equal(0, emailSender.ReplyAttempts);
        Assert.Equal(0, notifier.Attempts);
    }

    [Fact]
    public async Task Reply_ReturnsNotFound_WhenTheTicketIsUnknown()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        ReplyToSupportTicketCommandHandler handler =
            new(
                dbContext,
                new FixedDateTimeProvider(),
                new FakeEmailSender(),
                new FakeNotifier());

        Result<SupportTicketReplyDto> result =
            await handler.Handle(
                new ReplyToSupportTicketCommand(
                    new SupportTicketId(Guid.NewGuid()),
                    AdminId,
                    "Nobody is home."),
                CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(SupportTicketErrors.NotFound, result.Error);
        Assert.Equal(0, dbContext.SaveChangesCalls);
    }

    [Fact]
    public async Task Reply_StaysReadable_WhenEmailAndNotificationBothFail()
    {
        await using IdentityTestDbContext dbContext =
            CreateDbContext();

        SupportTicket ticket =
            await SeedTicketAsync(
                dbContext,
                SupportTicketStatus.New);

        FakeEmailSender emailSender = new() { ThrowOnReply = true };
        FakeNotifier notifier = new() { Throw = true };

        ReplyToSupportTicketCommandHandler handler =
            new(
                dbContext,
                new FixedDateTimeProvider(),
                emailSender,
                notifier);

        Result<SupportTicketReplyDto> result =
            await handler.Handle(
                new ReplyToSupportTicketCommand(
                    ticket.Id,
                    AdminId,
                    "We are on it."),
                CancellationToken.None);

        // Delivery is attempted and may fail; the saved reply is never lost
        // and is never reported as delivered.
        Assert.True(result.IsSuccess);
        Assert.Equal("We are on it.", result.Value.Body);

        Assert.Equal(1, emailSender.ReplyAttempts);
        Assert.Equal(1, notifier.Attempts);

        GetMySupportTicketByIdQueryHandler readHandler = new(dbContext);

        Result<MySupportTicketDetail> readback =
            await readHandler.Handle(
                new GetMySupportTicketByIdQuery(
                    ticket.UserId,
                    ticket.Id),
                CancellationToken.None);

        Assert.True(readback.IsSuccess);
        SupportTicketReplyDto reply =
            Assert.Single(readback.Value.Replies);
        Assert.Equal("We are on it.", reply.Body);
        Assert.Equal(AdminId.Value, reply.ActorUserId);
    }

    private static IdentityTestDbContext CreateDbContext()
    {
        DbContextOptions<IdentityTestDbContext> options =
            new DbContextOptionsBuilder<IdentityTestDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

        return new IdentityTestDbContext(options);
    }

    private static async Task<SupportTicket> SeedTicketAsync(
        IdentityTestDbContext dbContext,
        SupportTicketStatus status)
    {
        // A real ticket always belongs to a persisted user; the reply handler
        // can only attempt email when that row (and its address) exists.
        User owner =
            User.Create(
                FullName.Create("محمد أحمد"),
                FullName.Create("Mohamed Ahmed"),
                Email.Create($"{Guid.NewGuid():N}@example.com"),
                PhoneNumber.Create("+201001234567"));

        dbContext.Users.Add(owner);

        SupportTicket ticket =
            SupportTicket.Create(
                owner.Id,
                "Help needed",
                "I need assistance with my account.",
                Now.AddHours(-2));

        if (status != SupportTicketStatus.New)
        {
            ticket.ChangeStatus(
                status,
                AdminId,
                Now.AddHours(-1));
        }

        dbContext.SupportTickets.Add(ticket);

        await dbContext.SaveChangesAsync();
        dbContext.ResetSaveChangesCalls();

        return ticket;
    }

    private sealed class FixedDateTimeProvider :
        IDateTimeProvider
    {
        public DateTime UtcNow => Now;
    }

    private sealed class FakeEmailSender :
        IEmailSender
    {
        internal bool ThrowOnReply { get; set; }

        internal int ReplyAttempts { get; private set; }

        public Task SendEmailAsync(
            string email,
            string subject,
            string body,
            string? correlationId,
            CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task SendVerificationCodeAsync(
            string email,
            string code,
            VerificationPurpose purpose,
            CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task SendFamilyInvitationAsync(
            string email,
            string familyName,
            string inviteLink,
            CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task SendSupportRequestAsync(
            string senderName,
            string? senderEmail,
            string senderPhoneNumber,
            string accountTypes,
            string subject,
            string message,
            CancellationToken cancellationToken)
            => Task.CompletedTask;

        public async Task SendSupportReplyAsync(
            string? recipientEmail,
            string subject,
            string replyBody,
            CancellationToken cancellationToken)
        {
            ReplyAttempts++;

            if (ThrowOnReply)
            {
                await Task.Yield();

                throw new InvalidOperationException(
                    "Email delivery is unavailable.");
            }
        }
    }

    private sealed class FakeNotifier :
        ISupportReplyNotifier
    {
        internal bool Throw { get; set; }

        internal int Attempts { get; private set; }

        public async Task TryNotifyAsync(
            Guid recipientUserId,
            Guid ticketId,
            string body,
            CancellationToken cancellationToken)
        {
            Attempts++;

            if (Throw)
            {
                await Task.Yield();

                throw new InvalidOperationException(
                    "Notification delivery is unavailable.");
            }
        }
    }
}
