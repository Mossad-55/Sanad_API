using System.Reflection;
using System.Text.Json;
using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.Abstractions;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.BuildingBlocks.Domain.ValueObjects;
using Ahlena.Modules.Identity.Application.Abstractions.Messaging;
using Ahlena.Modules.Identity.Application.Support;
using Ahlena.Modules.Identity.Application.Users;
using Ahlena.Modules.Identity.Domain.Authentication.VerificationRequests;
using Ahlena.Modules.Identity.Domain.Support;
using Ahlena.Modules.Identity.Domain.Users;
using Ahlena.Modules.Identity.Infrastructure.Persistence;
using Ahlena.UnitTests.Identity.Registration;

namespace Ahlena.UnitTests.Identity.Support;

/// <summary>
/// F5 admin ticket workflow. Pins the approved numeric status contract, the
/// forward-only transition matrix (nothing leaves Closed, no reopen, no
/// no-op), durable replies carrying actor and time, and the rule that a saved
/// reply stays readable when email and in-app delivery both fail. It also pins
/// the admin inbox sender enrichment — the sender's display name and
/// first-registered account type, resolved through the existing Identity
/// display read in one batched lookup per page, null when the sender is
/// anonymized or missing, and the sender's email and phone are never returned.
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

    [Fact]
    public async Task Inbox_publishes_the_sender_display_name_and_account_type_and_never_the_channel_fields()
    {
        await using IdentityStore store = await IdentityStore.CreateAsync();
        IdentityDbContext identity = store.Identity;

        User submitter = IdentityUser(
            "+201000000201", "submitter@example.test", "سالم المستخدم", "Salem Submitter");
        submitter.AddAccount(AccountType.Family);
        identity.Users.Add(submitter);

        SupportTicket ticket = SupportTicket.Create(
            submitter.Id, "Cannot sign in", "I cannot sign in to my account.", Now.AddHours(-2));
        identity.SupportTickets.Add(ticket);
        await identity.SaveChangesAsync();

        var sender = new ForwardingSender(identity);
        var list = await new GetAdminSupportTicketsQueryHandler(identity, sender)
            .Handle(new GetAdminSupportTicketsQuery(), default);
        var detail = await new GetAdminSupportTicketByIdQueryHandler(identity, sender)
            .Handle(new GetAdminSupportTicketByIdQuery(ticket.Id), default);

        Assert.True(list.IsSuccess);
        AdminSupportTicketListItem row = Assert.Single(list.Value.Items);
        Assert.Equal(submitter.Id.Value, row.UserId);
        // The English full name wins over the Arabic one.
        Assert.Equal("Salem Submitter", row.DisplayName);
        Assert.Equal((int)AccountType.Family, row.AccountType);

        Assert.True(detail.IsSuccess);
        Assert.Equal("Salem Submitter", detail.Value.DisplayName);
        Assert.Equal((int)AccountType.Family, detail.Value.AccountType);

        // The privacy contract: neither read leaks the sender's email or phone.
        foreach (string json in new[]
                 {
                     JsonSerializer.Serialize(list.Value),
                     JsonSerializer.Serialize(detail.Value)
                 })
        {
            Assert.DoesNotContain("submitter@example.test", json, StringComparison.Ordinal);
            Assert.DoesNotContain("+201000000201", json, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Inbox_sender_fields_stay_null_for_an_anonymized_or_missing_sender_without_failing()
    {
        await using IdentityStore store = await IdentityStore.CreateAsync();
        IdentityDbContext identity = store.Identity;

        User anonymized = IdentityUser(
            "+201000000202", "gone@example.test", "مستخدم قديم", "Gone User");
        anonymized.AddAccount(AccountType.Family);
        identity.Users.Add(anonymized);

        SupportTicket anonymizedTicket = SupportTicket.Create(
            anonymized.Id, "Old request", "An old request.", Now.AddHours(-3));
        SupportTicket strangerTicket = SupportTicket.Create(
            UserId.New(), "Unknown sender", "No such user row.", Now.AddHours(-2));
        identity.SupportTickets.AddRange(anonymizedTicket, strangerTicket);
        await identity.SaveChangesAsync();

        anonymized.AnonymizeAndDeactivate(Now.AddHours(-1));
        await identity.SaveChangesAsync();

        var sender = new ForwardingSender(identity);
        var list = await new GetAdminSupportTicketsQueryHandler(identity, sender)
            .Handle(new GetAdminSupportTicketsQuery(), default);
        var anonymizedDetail = await new GetAdminSupportTicketByIdQueryHandler(identity, sender)
            .Handle(new GetAdminSupportTicketByIdQuery(anonymizedTicket.Id), default);
        var strangerDetail = await new GetAdminSupportTicketByIdQueryHandler(identity, sender)
            .Handle(new GetAdminSupportTicketByIdQuery(strangerTicket.Id), default);

        Assert.True(list.IsSuccess);
        Assert.Equal(2, list.Value.Items.Count);
        Assert.All(list.Value.Items, item =>
        {
            Assert.Null(item.DisplayName);
            Assert.Null(item.AccountType);
        });

        // The anonymized row is filtered out by the reused display read, and the
        // stranger has no row at all: both degrade to null, never an invented
        // placeholder and never a failure.
        Assert.True(anonymizedDetail.IsSuccess);
        Assert.Null(anonymizedDetail.Value.DisplayName);
        Assert.Null(anonymizedDetail.Value.AccountType);

        Assert.True(strangerDetail.IsSuccess);
        Assert.Null(strangerDetail.Value.DisplayName);
        Assert.Null(strangerDetail.Value.AccountType);
    }

    [Fact]
    public async Task Inbox_resolves_every_sender_of_a_page_in_one_batched_lookup()
    {
        await using IdentityStore store = await IdentityStore.CreateAsync();
        IdentityDbContext identity = store.Identity;

        List<User> submitters = [];
        for (int index = 1; index <= 3; index++)
        {
            User user = IdentityUser(
                $"+20100000030{index}",
                $"sender{index}@example.test",
                $"مرسل {index}",
                $"Sender {index}");
            user.AddAccount(AccountType.Family);
            submitters.Add(user);
            identity.SupportTickets.Add(SupportTicket.Create(
                user.Id, $"Request {index}", "Please help.", Now.AddHours(-index)));
        }

        identity.Users.AddRange(submitters);
        await identity.SaveChangesAsync();

        var sender = new ForwardingSender(identity);
        var list = await new GetAdminSupportTicketsQueryHandler(identity, sender)
            .Handle(new GetAdminSupportTicketsQuery(), default);

        Assert.True(list.IsSuccess);
        Assert.Equal(3, list.Value.Items.Count);

        // One lookup for the whole page (never one per row) and it carries every
        // distinct sender id.
        GetUserDisplayInfoByIdsQuery lookup = Assert.Single(sender.Requests);
        Assert.Equal(
            [.. submitters.Select(user => user.Id.Value).OrderBy(id => id)],
            [.. lookup.UserIds.Select(id => id.Value).OrderBy(id => id)]);
    }

    [Fact]
    public async Task Inbox_detail_resolves_its_single_sender_in_exactly_one_lookup()
    {
        await using IdentityStore store = await IdentityStore.CreateAsync();
        IdentityDbContext identity = store.Identity;

        User submitter = IdentityUser(
            "+201000000203", "detail@example.test", "سالم المستخدم", "Salem Submitter");
        submitter.AddAccount(AccountType.Family);
        identity.Users.Add(submitter);

        SupportTicket ticket = SupportTicket.Create(
            submitter.Id, "Cannot sign in", "I cannot sign in to my account.", Now.AddHours(-2));
        identity.SupportTickets.Add(ticket);
        await identity.SaveChangesAsync();

        var sender = new ForwardingSender(identity);
        var detail = await new GetAdminSupportTicketByIdQueryHandler(identity, sender)
            .Handle(new GetAdminSupportTicketByIdQuery(ticket.Id), default);

        Assert.True(detail.IsSuccess);
        Assert.Equal("Salem Submitter", detail.Value.DisplayName);
        Assert.Equal((int)AccountType.Family, detail.Value.AccountType);

        // One ticket resolves its one sender in exactly one lookup, and that
        // lookup carries exactly that sender.
        GetUserDisplayInfoByIdsQuery lookup = Assert.Single(sender.Requests);
        Assert.Equal([submitter.Id.Value], lookup.UserIds.Select(id => id.Value).ToArray());
    }

    [Fact]
    public void Inbox_dto_publishes_the_sender_facts_as_nullable_and_exposes_no_channel_field()
    {
        var nullability = new NullabilityInfoContext();

        foreach (Type type in new[] { typeof(AdminSupportTicketListItem), typeof(AdminSupportTicketDetail) })
        {
            Assert.DoesNotContain(
                type.GetProperties(),
                property =>
                    property.Name.Contains("Email", StringComparison.OrdinalIgnoreCase) ||
                    property.Name.Contains("Phone", StringComparison.OrdinalIgnoreCase));

            PropertyInfo displayName = Assert.IsAssignableFrom<PropertyInfo>(type.GetProperty("DisplayName"));
            PropertyInfo accountType = Assert.IsAssignableFrom<PropertyInfo>(type.GetProperty("AccountType"));

            Assert.Equal(typeof(string), displayName.PropertyType);
            Assert.Equal(typeof(int?), accountType.PropertyType);
            Assert.Equal(NullabilityState.Nullable, nullability.Create(displayName).ReadState);
            Assert.Equal(NullabilityState.Nullable, nullability.Create(accountType).ReadState);
        }
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

    private static User IdentityUser(
        string phone,
        string email,
        string arabicFullName,
        string englishFullName) =>
        User.Create(
            FullName.Create(arabicFullName),
            FullName.Create(englishFullName),
            Email.Create(email),
            PhoneNumber.Create(phone));

    /// <summary>
    /// A real relational Identity store. The ticket inbox resolves sender
    /// display facts through the existing Identity display read, which reads the
    /// <c>Accounts</c> owned collection — so the enrichment is exercised against
    /// the real mapping instead of a hand-written double.
    /// </summary>
    private sealed class IdentityStore : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private IdentityStore(
            SqliteConnection connection,
            IdentityDbContext identity)
        {
            _connection = connection;
            Identity = identity;
        }

        public IdentityDbContext Identity { get; }

        public static async Task<IdentityStore> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var identity = new IdentityDbContext(
                new DbContextOptionsBuilder<IdentityDbContext>()
                    .UseSqlite(connection)
                    .Options);
            await identity.Database.EnsureCreatedAsync();

            return new IdentityStore(connection, identity);
        }

        public async ValueTask DisposeAsync()
        {
            await Identity.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    /// <summary>
    /// Forwards the sender-enrichment lookup to the real
    /// <see cref="GetUserDisplayInfoByIdsQueryHandler"/> and records every
    /// request, so a test can pin that the inbox batches one lookup per page.
    /// </summary>
    private sealed class ForwardingSender(IdentityDbContext identity) : ISender
    {
        internal List<GetUserDisplayInfoByIdsQuery> Requests { get; } = [];

        public async Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            if (request is GetUserDisplayInfoByIdsQuery query)
            {
                Requests.Add(query);
                var result = await new GetUserDisplayInfoByIdsQueryHandler(identity)
                    .Handle(query, cancellationToken);
                return (TResponse)(object)result;
            }

            throw new NotSupportedException();
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
