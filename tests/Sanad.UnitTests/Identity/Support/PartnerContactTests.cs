using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Identity.Application.Abstractions.Data;
using Sanad.Modules.Identity.Application.Support;
using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;
using Sanad.Modules.Identity.Domain.Authentication.VerificationRequests;
using Sanad.Modules.Identity.Domain.Support;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.Modules.Identity.Infrastructure.Persistence;
using FeedbackEntity = Sanad.Modules.Identity.Domain.Feedback;

namespace Sanad.UnitTests.Identity.Support;

/// <summary>
/// The anonymous partner-contact command path. The submitted details are
/// stored as request data in their own aggregate: no account is created, no
/// identity is faked, and the anti-spam rules (idempotency key and the
/// per-client submission window) are real.
/// </summary>
public sealed class PartnerContactTests
{
    private static readonly DateTime UtcNow =
        new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Submit_persists_the_submitted_request_data_and_creates_no_user_account()
    {
        await using IdentityDbContext dbContext = CreateDbContext();

        Result<PartnerContactResponse> result = await CreateHandler(dbContext).Handle(
            new SubmitPartnerContactCommand(
                "  Nour Hassan  ",
                "  Hassani Care  ",
                "  nour@example.com  ",
                "  Partnership  ",
                "  We would like to discuss a partnership.  ",
                "key-1",
                "client-1"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        PartnerContact stored = await dbContext.PartnerContacts.SingleAsync();
        Assert.Equal(result.Value.ContactId, stored.Id);
        Assert.Equal("Nour Hassan", stored.Name);
        Assert.Equal("Hassani Care", stored.Company);
        Assert.Equal("nour@example.com", stored.Email);
        Assert.Equal("Partnership", stored.Subject);
        Assert.Equal("We would like to discuss a partnership.", stored.Message);
        Assert.Equal("key-1", stored.IdempotencyKey);
        Assert.Equal("client-1", stored.ClientKey);
        Assert.Equal(UtcNow, stored.CreatedOnUtc);

        // The submitted request never becomes an account: the users table is
        // still empty and no user id is stored on the contact.
        Assert.Empty(dbContext.Users);
    }

    [Fact]
    public async Task Submit_rejects_invalid_input_and_persists_nothing()
    {
        await using IdentityDbContext dbContext = CreateDbContext();

        Result<PartnerContactResponse> result = await CreateHandler(dbContext).Handle(
            new SubmitPartnerContactCommand(
                "N",
                null,
                "not-an-email",
                null,
                "too short",
                "key-1",
                "client-1"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PartnerContactErrors.Invalid.Code, result.Error.Code);
        Assert.Empty(dbContext.PartnerContacts);
        Assert.Empty(dbContext.Users);
    }

    [Fact]
    public async Task Submit_rejects_a_missing_idempotency_key_and_persists_nothing()
    {
        await using IdentityDbContext dbContext = CreateDbContext();

        Result<PartnerContactResponse> result = await CreateHandler(dbContext).Handle(
            new SubmitPartnerContactCommand(
                "Nour Hassan",
                null,
                "nour@example.com",
                null,
                "We would like to discuss a partnership.",
                "   ",
                "client-1"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PartnerContactErrors.Invalid.Code, result.Error.Code);
        Assert.Empty(dbContext.PartnerContacts);
    }

    [Fact]
    public void Validator_rejects_an_incomplete_submission()
    {
        var validator = new SubmitPartnerContactCommandValidator();

        var result = validator.Validate(
            new SubmitPartnerContactCommand(
                "  ",
                "company",
                "not-an-email",
                "subject",
                "short",
                "",
                ""));

        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(SubmitPartnerContactCommand.Name));
        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(SubmitPartnerContactCommand.Email));
        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(SubmitPartnerContactCommand.Message));
        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(SubmitPartnerContactCommand.IdempotencyKey));
        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(SubmitPartnerContactCommand.ClientKey));
    }

    [Fact]
    public async Task Submit_is_idempotent_for_the_same_key_and_the_same_content()
    {
        await using IdentityDbContext dbContext = CreateDbContext();
        SubmitPartnerContactCommandHandler handler = CreateHandler(dbContext);

        var command = new SubmitPartnerContactCommand(
            "Nour Hassan",
            null,
            "nour@example.com",
            null,
            "We would like to discuss a partnership.",
            "key-1",
            "client-1");

        Result<PartnerContactResponse> first = await handler.Handle(command, CancellationToken.None);
        Result<PartnerContactResponse> retry = await handler.Handle(command, CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(retry.IsSuccess);
        Assert.Equal(first.Value.ContactId, retry.Value.ContactId);

        // A retry never creates a second row.
        Assert.Single(await dbContext.PartnerContacts.ToListAsync());
    }

    [Fact]
    public async Task Submit_conflicts_when_the_same_key_is_reused_with_different_content()
    {
        await using IdentityDbContext dbContext = CreateDbContext();
        SubmitPartnerContactCommandHandler handler = CreateHandler(dbContext);

        Result<PartnerContactResponse> first = await handler.Handle(
            new SubmitPartnerContactCommand(
                "Nour Hassan",
                null,
                "nour@example.com",
                null,
                "We would like to discuss a partnership.",
                "key-1",
                "client-1"),
            CancellationToken.None);

        Result<PartnerContactResponse> conflicting = await handler.Handle(
            new SubmitPartnerContactCommand(
                "Nour Hassan",
                null,
                "nour@example.com",
                null,
                "A completely different message body.",
                "key-1",
                "client-1"),
            CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(conflicting.IsFailure);
        Assert.Equal(PartnerContactErrors.Conflict.Code, conflicting.Error.Code);
        Assert.Single(await dbContext.PartnerContacts.ToListAsync());
    }

    [Fact]
    public async Task Submit_rate_limits_the_submissions_of_one_client_inside_the_window()
    {
        await using IdentityDbContext dbContext = CreateDbContext();
        SubmitPartnerContactCommandHandler handler = CreateHandler(dbContext);

        for (int index = 0; index < SubmitPartnerContactCommandHandler.MaxSubmissionsPerWindowPerClient; index++)
        {
            Result<PartnerContactResponse> accepted = await handler.Handle(
                Command($"key-{index}"),
                CancellationToken.None);

            Assert.True(accepted.IsSuccess);
        }

        Result<PartnerContactResponse> limited = await handler.Handle(
            Command("key-over-the-limit"),
            CancellationToken.None);

        Assert.True(limited.IsFailure);
        Assert.Equal(PartnerContactErrors.RateLimitedCode, limited.Error.Code);
        Assert.NotNull(limited.Error.RetryAfter);
        Assert.True(limited.Error.RetryAfter! > TimeSpan.Zero);

        // The refused submission was not stored.
        Assert.Equal(
            SubmitPartnerContactCommandHandler.MaxSubmissionsPerWindowPerClient,
            await dbContext.PartnerContacts.CountAsync());
    }

    [Fact]
    public async Task Admin_reads_return_the_submitted_data_and_never_the_client_key()
    {
        await using IdentityDbContext dbContext = CreateDbContext();
        await CreateHandler(dbContext).Handle(
            Command("key-1"),
            CancellationToken.None);

        var page = await new GetAdminPartnerContactsQueryHandler(dbContext).Handle(
            new GetAdminPartnerContactsQuery(),
            CancellationToken.None);

        Assert.True(page.IsSuccess);
        AdminPartnerContactListItem item = Assert.Single(page.Value.Items);
        Assert.Equal("Nour Hassan", item.Name);
        Assert.Equal("nour@example.com", item.Email);
        Assert.Null(item.Company);
        Assert.Null(item.Subject);

        // The anti-spam client key is never part of an admin read.
        string serialized = System.Text.Json.JsonSerializer.Serialize(page.Value);
        Assert.DoesNotContain("client-1", serialized, StringComparison.Ordinal);

        var detail = await new GetAdminPartnerContactByIdQueryHandler(dbContext).Handle(
            new GetAdminPartnerContactByIdQuery(item.ContactId),
            CancellationToken.None);

        Assert.True(detail.IsSuccess);
        Assert.Equal("We would like to discuss a partnership.", detail.Value.Message);
        Assert.Equal(UtcNow, detail.Value.CreatedOnUtc);

        var missing = await new GetAdminPartnerContactByIdQueryHandler(dbContext).Handle(
            new GetAdminPartnerContactByIdQuery(Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(missing.IsFailure);
        Assert.Equal(PartnerContactErrors.NotFound.Code, missing.Error.Code);
    }

    [Fact]
    public async Task Submit_returns_the_persisted_winner_when_a_concurrent_retry_lost_the_unique_race()
    {
        string databaseName = NewDatabaseName();
        await using IdentityDbContext inner = CreateDbContext(databaseName);
        var racing = new RacingIdentityDbContext(
            inner,
            databaseName,
            winner => winner.PartnerContacts.Add(PartnerContact.Create(
                "Nour Hassan",
                null,
                "nour@example.com",
                null,
                "We would like to discuss a partnership.",
                "key-1",
                "client-1",
                UtcNow)));

        Result<PartnerContactResponse> result = await CreateHandler(racing).Handle(
            new SubmitPartnerContactCommand(
                "Nour Hassan",
                null,
                "nour@example.com",
                null,
                "We would like to discuss a partnership.",
                "key-1",
                "client-1"),
            CancellationToken.None);

        // The raced-out request answers with the winner's receipt, and the
        // failed insert left nothing behind: the store holds the winner only.
        Assert.True(result.IsSuccess);
        PartnerContact winner = await inner.PartnerContacts.SingleAsync();
        Assert.Equal(winner.Id, result.Value.ContactId);
        Assert.Equal(UtcNow, result.Value.SubmittedOnUtc);
        Assert.Empty(await inner.PartnerContacts
            .Where(contact => contact.Id != winner.Id)
            .ToListAsync());
    }

    [Fact]
    public async Task Submit_conflicts_when_a_concurrent_race_won_with_different_content()
    {
        string databaseName = NewDatabaseName();
        await using IdentityDbContext inner = CreateDbContext(databaseName);
        var racing = new RacingIdentityDbContext(
            inner,
            databaseName,
            winner => winner.PartnerContacts.Add(PartnerContact.Create(
                "Nour Hassan",
                null,
                "nour@example.com",
                null,
                "A completely different message body.",
                "key-1",
                "client-1",
                UtcNow)));

        Result<PartnerContactResponse> result = await CreateHandler(racing).Handle(
            new SubmitPartnerContactCommand(
                "Nour Hassan",
                null,
                "nour@example.com",
                null,
                "We would like to discuss a partnership.",
                "key-1",
                "client-1"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PartnerContactErrors.Conflict.Code, result.Error.Code);
        Assert.Single(await inner.PartnerContacts.ToListAsync());
    }

    [Fact]
    public async Task Submit_propagates_a_unique_violation_when_no_winner_was_persisted()
    {
        string databaseName = NewDatabaseName();
        await using IdentityDbContext inner = CreateDbContext(databaseName);
        var racing = new RacingIdentityDbContext(inner, databaseName, null);

        // No winner exists: the failure must not be reported as a success.
        DbUpdateException failure = await Assert.ThrowsAsync<DbUpdateException>(
            () => CreateHandler(racing).Handle(
                new SubmitPartnerContactCommand(
                    "Nour Hassan",
                    null,
                    "nour@example.com",
                    null,
                    "We would like to discuss a partnership.",
                    "key-1",
                    "client-1"),
                CancellationToken.None));

        Assert.Contains("23505", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await inner.PartnerContacts.ToListAsync());
    }

    [Fact]
    public async Task Submit_does_not_swallow_an_unrelated_persistence_failure()
    {
        string databaseName = NewDatabaseName();
        await using IdentityDbContext inner = CreateDbContext(databaseName);
        var racing = new RacingIdentityDbContext(
            inner,
            databaseName,
            null,
            new InvalidOperationException("the database connection was reset"));

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateHandler(racing).Handle(
                new SubmitPartnerContactCommand(
                    "Nour Hassan",
                    null,
                    "nour@example.com",
                    null,
                    "We would like to discuss a partnership.",
                    "key-1",
                    "client-1"),
                CancellationToken.None));

        Assert.Equal("the database connection was reset", failure.Message);
        Assert.Empty(await inner.PartnerContacts.ToListAsync());
    }

    [Fact]
    public async Task Submit_does_not_swallow_a_cancellation()
    {
        string databaseName = NewDatabaseName();
        await using IdentityDbContext inner = CreateDbContext(databaseName);
        var racing = new RacingIdentityDbContext(
            inner,
            databaseName,
            null,
            new OperationCanceledException("cancelled"));

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => CreateHandler(racing).Handle(
                new SubmitPartnerContactCommand(
                    "Nour Hassan",
                    null,
                    "nour@example.com",
                    null,
                    "We would like to discuss a partnership.",
                    "key-1",
                    "client-1"),
                CancellationToken.None));

        Assert.Empty(await inner.PartnerContacts.ToListAsync());
    }

    private static SubmitPartnerContactCommand Command(string idempotencyKey) =>
        new(
            "Nour Hassan",
            null,
            "nour@example.com",
            null,
            "We would like to discuss a partnership.",
            idempotencyKey,
            "client-1");

    private static SubmitPartnerContactCommandHandler CreateHandler(
        IIdentityDbContext dbContext) =>
        new(dbContext, new FixedDateTimeProvider());

    private static string NewDatabaseName() =>
        Guid.NewGuid().ToString();

    private static IdentityDbContext CreateDbContext() =>
        CreateDbContext(NewDatabaseName());

    private static IdentityDbContext CreateDbContext(
        string databaseName)
    {
        DbContextOptions<IdentityDbContext> options =
            new DbContextOptionsBuilder<IdentityDbContext>()
                .UseInMemoryDatabase(databaseName)
                .Options;

        return new IdentityDbContext(options);
    }

    /// <summary>
    /// Simulates the persistence race on the unique (client key, idempotency
    /// key) index: the first save is rejected by the store, optionally after a
    /// concurrent request persisted the winning row through its own context
    /// over the same store.
    /// </summary>
    private sealed class RacingIdentityDbContext(
        IdentityDbContext inner,
        string databaseName,
        Action<IdentityDbContext>? persistWinner,
        Exception? failure = null) : IIdentityDbContext
    {
        private int _saves;

        public DbSet<User> Users => inner.Users;

        public DbSet<VerificationRequest> VerificationRequests => inner.VerificationRequests;

        public DbSet<DeviceSession> DeviceSessions => inner.DeviceSessions;

        public DbSet<SupportTicket> SupportTickets => inner.SupportTickets;

        public DbSet<SupportTicketReply> SupportTicketReplies => inner.SupportTicketReplies;

        public DbSet<FeedbackEntity> Feedbacks => inner.Feedbacks;

        public DbSet<AdminUserAction> AdminUserActions => inner.AdminUserActions;

        public DbSet<PartnerContact> PartnerContacts => inner.PartnerContacts;

        public Task<int> SaveChangesAsync(
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _saves) != 1)
            {
                return inner.SaveChangesAsync(cancellationToken);
            }

            if (persistWinner is not null)
            {
                using var winnerContext = new IdentityDbContext(
                    new DbContextOptionsBuilder<IdentityDbContext>()
                        .UseInMemoryDatabase(databaseName)
                        .Options);

                persistWinner(winnerContext);
                winnerContext.SaveChangesAsync();
            }

            if (failure is not null)
            {
                throw failure;
            }

            throw new DbUpdateException(
                "23505: duplicate key value violates unique constraint " +
                "\"IX_partner_contacts_client_key_idempotency_key\"",
                new InvalidOperationException("23505 duplicate key"));
        }
    }

    private sealed class FixedDateTimeProvider : IDateTimeProvider
    {
        private static readonly DateTime Value =
            new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);

        public DateTime UtcNow => Value;
    }
}
