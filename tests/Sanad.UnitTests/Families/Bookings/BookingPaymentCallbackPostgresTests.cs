using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Families.Application.Bookings;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Infrastructure.Payments;
using Sanad.Modules.Families.Infrastructure.Persistence;
using Xunit;

namespace Sanad.UnitTests.Families.Bookings;

/// <summary>
/// Card checkout callback persistence proof for the family caregiver-booking
/// payment path (<see cref="ConfirmBookingPaymentCommandHandler"/> — the exact
/// handler the Paymob webhook drives for card callbacks) against a validated
/// disposable PostgreSQL target.
/// <para>
/// Safety: the target credentials are derived from
/// <c>ConnectionStrings__IdentityIntegrationDatabase</c>, whose host must be
/// loopback on port 5432; the database name is replaced with this process's
/// unique nonce-bearing name and verified absent before creation. Only the
/// committed Families migrations are applied (their own per-schema history
/// table, matching the W4-F3 harness). This class never connects to a
/// persistent database, uses the <see cref="DevelopmentPaymobClient"/> double
/// so no provider call can occur, and deletes only the unique database it
/// created. Each test tears down that whole database (all of its rows with
/// it); there is no per-row cleanup. Teardown is armed immediately after the
/// explicit CREATE DATABASE, so even a failed migration leaves nothing behind.
/// </para>
/// <para>
/// This proves Sanad-side callback handling and persistence. A real Paymob
/// sandbox checkout additionally needs the rotated test-mode secret key and a
/// public callback destination; those remain external prerequisites.
/// </para>
/// </summary>
[Collection("BookingCardCallbackVerification")]
public sealed class BookingPaymentCallbackPostgresTests : IAsyncLifetime
{
    private const string ConnectionStringVariable =
        "ConnectionStrings__IdentityIntegrationDatabase";

    private static readonly string ExpectedDatabase =
        "SanadCardCbVerify_" + Guid.NewGuid().ToString("N");

    private static readonly DateTime FixedUtcNow =
        new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);

    private static readonly SemaphoreSlim MigrationLock = new(1, 1);

    private static string? _migratedFor;

    // Non-null from the moment this process creates the tracked database until
    // its teardown completes; it is the only connection string teardown uses.
    private static string? _createdFor;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_createdFor is null)
        {
            return;
        }

        // The target name was verified absent before this process created it,
        // so teardown can only ever drop the database this process created —
        // never SanadDb or any fixed-name database. Because the target is
        // tracked from creation (before migration), this teardown also runs
        // after a failed migration; the flags clear only once the drop
        // succeeded, so a failed drop is attempted again by the next teardown.
        await using FamiliesDbContext families = CreateFamiliesDb(_createdFor);
        await families.Database.EnsureDeletedAsync();
        _createdFor = null;
        _migratedFor = null;
    }

    [CardCallbackPostgresFact]
    public async Task SuccessfulCardCallback_PersistsPaidBookingState()
    {
        string connectionString = GetValidatedConnectionString();
        await EnsureMigratedAsync(connectionString);
        SeedFixture seed = await SeedBookingAsync(connectionString, "cb-success");

        string orderId = $"cbk-{seed.Token}";
        await using FamiliesDbContext context = CreateFamiliesDb(connectionString);
        ConfirmBookingPaymentCommandHandler handler = new(
            context,
            new DevelopmentPaymobClient());

        long amountCents = await ReadExpectedCentsAsync(connectionString, orderId);
        Result<ConfirmBookingPaymentResponse> result = await handler.Handle(
            new ConfirmBookingPaymentCommand(orderId, 778_001L, amountCents, Success: true, Pending: false, FixedUtcNow.AddMinutes(2)),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error.Message);
        Assert.Equal("Paid", result.Value.Outcome);

        // Fresh context proves durable persistence, not tracked-entity state.
        await using FamiliesDbContext readback = CreateFamiliesDb(connectionString);
        Booking booking = await readback.Bookings.AsNoTracking()
            .SingleAsync(b => b.Id == seed.BookingId);
        Assert.Equal(BookingStatus.PendingCaregiverApproval, booking.Status);
        Assert.Equal(orderId, booking.PaymobOrderId);
        Assert.Equal("778001", booking.PaymobTransactionId);
        Assert.Equal(FixedUtcNow.AddMinutes(2), booking.PaidOnUtc);
        PaymentTransaction transaction = Assert.Single(booking.PaymentTransactions);
        Assert.Equal(PaymentTransactionStatus.Succeeded, transaction.Status);
        Assert.Equal("778001", transaction.PaymobTransactionId);
        Assert.Null(transaction.FailedOnUtc);
    }

    [CardCallbackPostgresFact]
    public async Task AmountMismatchCallback_IsRefused_WithoutStateChange()
    {
        string connectionString = GetValidatedConnectionString();
        await EnsureMigratedAsync(connectionString);
        SeedFixture seed = await SeedBookingAsync(connectionString, "cb-mismatch");

        string orderId = $"cbk-{seed.Token}";
        long amountCents = await ReadExpectedCentsAsync(connectionString, orderId);
        await using FamiliesDbContext context = CreateFamiliesDb(connectionString);
        ConfirmBookingPaymentCommandHandler handler = new(
            context,
            new DevelopmentPaymobClient());

        Result<ConfirmBookingPaymentResponse> result = await handler.Handle(
            new ConfirmBookingPaymentCommand(orderId, 778_002L, amountCents + 1, Success: true, Pending: false, FixedUtcNow.AddMinutes(2)),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Paymob.AmountMismatch", result.Error.Code);

        await using FamiliesDbContext readback = CreateFamiliesDb(connectionString);
        Booking booking = await readback.Bookings.AsNoTracking()
            .SingleAsync(b => b.Id == seed.BookingId);
        Assert.Equal(BookingStatus.PendingPayment, booking.Status);
        PaymentTransaction transaction = Assert.Single(booking.PaymentTransactions);
        Assert.Equal(PaymentTransactionStatus.Pending, transaction.Status);
        Assert.Null(transaction.SettledOnUtc);
    }

    [CardCallbackPostgresFact]
    public async Task DuplicateSuccessCallback_IsIdempotent_AlreadyProcessed()
    {
        string connectionString = GetValidatedConnectionString();
        await EnsureMigratedAsync(connectionString);
        SeedFixture seed = await SeedBookingAsync(connectionString, "cb-duplicate");

        string orderId = $"cbk-{seed.Token}";
        long amountCents = await ReadExpectedCentsAsync(connectionString, orderId);
        await using FamiliesDbContext context = CreateFamiliesDb(connectionString);
        ConfirmBookingPaymentCommandHandler handler = new(
            context,
            new DevelopmentPaymobClient());

        Result<ConfirmBookingPaymentResponse> first = await handler.Handle(
            new ConfirmBookingPaymentCommand(orderId, 778_003L, amountCents, Success: true, Pending: false, FixedUtcNow.AddMinutes(2)),
            CancellationToken.None);
        Assert.True(first.IsSuccess, first.Error.Message);
        Assert.Equal("Paid", first.Value.Outcome);

        Result<ConfirmBookingPaymentResponse> duplicate = await handler.Handle(
            new ConfirmBookingPaymentCommand(orderId, 999_003L, amountCents, Success: true, Pending: false, FixedUtcNow.AddMinutes(9)),
            CancellationToken.None);

        Assert.True(duplicate.IsSuccess, duplicate.Error.Message);
        Assert.Equal("AlreadyProcessed", duplicate.Value.Outcome);

        await using FamiliesDbContext readback = CreateFamiliesDb(connectionString);
        Booking booking = await readback.Bookings.AsNoTracking()
            .SingleAsync(b => b.Id == seed.BookingId);
        PaymentTransaction transaction = Assert.Single(booking.PaymentTransactions);
        Assert.Equal(PaymentTransactionStatus.Succeeded, transaction.Status);
        Assert.Equal("778003", transaction.PaymobTransactionId);
    }

    [CardCallbackPostgresFact]
    public async Task FailedCardCallback_PersistsFailedTransaction()
    {
        string connectionString = GetValidatedConnectionString();
        await EnsureMigratedAsync(connectionString);
        SeedFixture seed = await SeedBookingAsync(connectionString, "cb-failed");

        string orderId = $"cbk-{seed.Token}";
        long amountCents = await ReadExpectedCentsAsync(connectionString, orderId);
        await using FamiliesDbContext context = CreateFamiliesDb(connectionString);
        ConfirmBookingPaymentCommandHandler handler = new(
            context,
            new DevelopmentPaymobClient());

        Result<ConfirmBookingPaymentResponse> result = await handler.Handle(
            new ConfirmBookingPaymentCommand(orderId, 778_004L, amountCents, Success: false, Pending: false, FixedUtcNow.AddMinutes(2)),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error.Message);
        Assert.Equal("Failed", result.Value.Outcome);

        await using FamiliesDbContext readback = CreateFamiliesDb(connectionString);
        Booking booking = await readback.Bookings.AsNoTracking()
            .SingleAsync(b => b.Id == seed.BookingId);
        Assert.Equal(BookingStatus.PendingPayment, booking.Status);
        PaymentTransaction transaction = Assert.Single(booking.PaymentTransactions);
        Assert.Equal(PaymentTransactionStatus.Failed, transaction.Status);
        Assert.Equal("778004", transaction.PaymobTransactionId);
    }

    [CardCallbackPostgresFact]
    public async Task PendingCallback_IsAcknowledged_WithoutStateChange()
    {
        string connectionString = GetValidatedConnectionString();
        await EnsureMigratedAsync(connectionString);
        SeedFixture seed = await SeedBookingAsync(connectionString, "cb-pending");

        string orderId = $"cbk-{seed.Token}";
        long amountCents = await ReadExpectedCentsAsync(connectionString, orderId);
        await using FamiliesDbContext context = CreateFamiliesDb(connectionString);
        ConfirmBookingPaymentCommandHandler handler = new(
            context,
            new DevelopmentPaymobClient());

        Result<ConfirmBookingPaymentResponse> result = await handler.Handle(
            new ConfirmBookingPaymentCommand(orderId, 778_005L, amountCents, Success: false, Pending: true, FixedUtcNow.AddMinutes(2)),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error.Message);
        Assert.Equal("Pending", result.Value.Outcome);

        await using FamiliesDbContext readback = CreateFamiliesDb(connectionString);
        Booking booking = await readback.Bookings.AsNoTracking()
            .SingleAsync(b => b.Id == seed.BookingId);
        Assert.Equal(BookingStatus.PendingPayment, booking.Status);
        PaymentTransaction transaction = Assert.Single(booking.PaymentTransactions);
        Assert.Equal(PaymentTransactionStatus.Pending, transaction.Status);
    }

    [CardCallbackPostgresFact]
    public async Task UnknownOrder_IsRefused_AsBookingsNotFound()
    {
        string connectionString = GetValidatedConnectionString();
        await EnsureMigratedAsync(connectionString);

        await using FamiliesDbContext context = CreateFamiliesDb(connectionString);
        ConfirmBookingPaymentCommandHandler handler = new(
            context,
            new DevelopmentPaymobClient());

        Result<ConfirmBookingPaymentResponse> result = await handler.Handle(
            new ConfirmBookingPaymentCommand("cbk-does-not-exist", 778_006L, 19_900, Success: true, Pending: false, FixedUtcNow),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Bookings.NotFound", result.Error.Code);
    }

    private static async Task<long> ReadExpectedCentsAsync(string connectionString, string orderId)
    {
        await using FamiliesDbContext db = CreateFamiliesDb(connectionString);
        Booking booking = await db.Bookings.AsNoTracking()
            .SingleAsync(b => b.PaymentTransactions.Any(t => t.PaymobOrderId == orderId));
        PaymentTransaction transaction = Assert.Single(
            booking.PaymentTransactions,
            t => t.PaymobOrderId == orderId);
        return (long)decimal.Round(transaction.Amount * 100m, 0, MidpointRounding.ToEven);
    }

    private sealed record SeedFixture(
        BookingId BookingId,
        FamilyId FamilyId,
        string Token);

    private static async Task<SeedFixture> SeedBookingAsync(string connectionString, string token)
    {
        await using FamiliesDbContext db = CreateFamiliesDb(connectionString);

        UserId owner = UserId.New();
        Family family = Family.Create(owner, $"card-callback {token}");
        Booking booking = Booking.Create(
            family.Id,
            owner,
            ElderlyId.New(),
            CaregiverId.New(),
            BookingCaregiverType.Companion,
            BookingShiftType.HomeVisit,
            DateOnly.FromDateTime(FixedUtcNow.AddDays(2)),
            new TimeOnly(10, 0),
            new TimeOnly(12, 0),
            "1 Verification Street, Nasr City",
            null,
            BookingPriceSnapshot.Calculate(199m, 0m, 14m, 1),
            FixedUtcNow.AddHours(2),
            DateOnly.FromDateTime(FixedUtcNow),
            FixedUtcNow);
        db.Families.Add(family);
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        string orderId = $"cbk-{token}";
        booking.RecordPaymentIntent(orderId, PaymentMethod.Card, FixedUtcNow.AddMinutes(1));
        await db.SaveChangesAsync();

        return new SeedFixture(booking.Id, family.Id, token);
    }

    private static async Task EnsureMigratedAsync(string connectionString)
    {
        if (_migratedFor == connectionString)
        {
            return;
        }

        await MigrationLock.WaitAsync();
        try
        {
            if (_migratedFor == connectionString)
            {
                return;
            }

            await EnsureFreshDatabaseAbsentAsync(connectionString);

            // Ownership chain: absence is proven, the empty target is created
            // explicitly and tracked immediately (so teardown runs even when
            // the migration below throws), and only a fully migrated target is
            // marked as migrated.
            await CreateTrackedDatabaseAsync(connectionString);

            await using FamiliesDbContext families = CreateFamiliesDb(connectionString);
            await families.Database.MigrateAsync();

            _migratedFor = connectionString;
        }
        finally
        {
            MigrationLock.Release();
        }
    }

    private static string GetValidatedConnectionString()
    {
        string? raw = Environment.GetEnvironmentVariable(ConnectionStringVariable);
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new InvalidOperationException(
                $"The card-callback persistence proof requires {ConnectionStringVariable}.");
        }

        var builder = new NpgsqlConnectionStringBuilder(raw);
        if (builder.Host is not ("localhost" or "127.0.0.1" or "::1") || builder.Port != 5432)
        {
            throw new InvalidOperationException(
                "Refusing to write: the card-callback verification source must be localhost:5432 PostgreSQL.");
        }

        builder.Database = ExpectedDatabase;
        return builder.ConnectionString;
    }

    private static async Task EnsureFreshDatabaseAbsentAsync(string connectionString)
    {
        var maintenance = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = "postgres"
        };

        await using var connection = new NpgsqlConnection(maintenance.ConnectionString);
        await connection.OpenAsync();

        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS (SELECT 1 FROM pg_database WHERE datname = @database)";
        command.Parameters.AddWithValue("database", ExpectedDatabase);
        bool exists = (bool)(await command.ExecuteScalarAsync() ?? false);

        if (exists)
        {
            throw new InvalidOperationException(
                "Refusing to write: the randomly named card-callback disposable target already exists; no database was changed.");
        }
    }

    private static async Task CreateTrackedDatabaseAsync(string connectionString)
    {
        var maintenance = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = "postgres"
        };

        await using var connection = new NpgsqlConnection(maintenance.ConnectionString);
        await connection.OpenAsync();

        // ExpectedDatabase is generated from Guid.NewGuid().ToString("N"), so
        // the quoted identifier is a plain ASCII name generated by this process.
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE \"{ExpectedDatabase}\"";
        await command.ExecuteNonQueryAsync();

        // Teardown is armed only after the create succeeded: from this moment
        // the unique target exists and is this process's to remove.
        _createdFor = connectionString;
    }

    private static FamiliesDbContext CreateFamiliesDb(string connectionString) =>
        new(new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable(
                    "__EFMigrationsHistory", FamiliesDbContext.Schema))
            .Options);

    private sealed class CardCallbackPostgresFactAttribute : FactAttribute
    {
        public CardCallbackPostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable(ConnectionStringVariable)))
            {
                Skip =
                    "Card-callback persistence proof is skipped. " +
                    "Set ConnectionStrings__IdentityIntegrationDatabase to a " +
                    "validated disposable PostgreSQL target to run it.";
            }
        }
    }
}

[CollectionDefinition("BookingCardCallbackVerification", DisableParallelization = true)]
public sealed class BookingCardCallbackVerificationCollection
{
}
