using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Application.Abstractions.Data;
using Sanad.Modules.CareHomes.Application.Bookings;
using Sanad.Modules.CareHomes.Domain.Bookings;
using Sanad.Modules.CareHomes.Domain.Facilities;
using Sanad.Modules.CareHomes.Domain.Idempotency;
using Sanad.Modules.CareHomes.Infrastructure.Persistence;
using Sanad.Modules.Families.Application.Abstractions.Payments;
using Xunit;

namespace Sanad.UnitTests.CareHomes;

/// <summary>
/// Relational proof for the verified-readback completion race, which the
/// in-memory provider cannot exercise (it ignores concurrency tokens). The
/// target is a uniquely named disposable database derived from
/// <c>ConnectionStrings__IdentityIntegrationDatabase</c>; the source host must
/// be loopback on port 5432, the generated name is verified absent before
/// creation, only the CareHomes migrations are applied, and only that exact
/// nonce database is dropped. It asserts the two directions of the completion
/// race: an unrelated concurrent writer must never be reported as this
/// processor's success, and a concurrent writer that completed the SAME refund
/// reference is accepted exactly once.
/// </summary>
[Collection("SliceSchemaVerification")]
public sealed class CareHomeRefundReadbackConcurrencyPostgresTests : IAsyncLifetime
{
    private const string ConnectionStringVariable =
        "ConnectionStrings__IdentityIntegrationDatabase";

    private static readonly string ExpectedDatabase =
        "SanadRefundRace_" + Guid.NewGuid().ToString("N");

    private static readonly SemaphoreSlim MigrationLock = new(1, 1);

    private static string? _migratedFor;

    private const long CaptureTransactionId = 561442756;
    private const long RefundTransactionId = 561442898;
    private const long MerchantOrderId = 640412142;
    private const long AmountCents = 1_150_000;

    private static readonly DateTime Now = new(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (string.IsNullOrWhiteSpace(_migratedFor))
        {
            return;
        }

        await using CareHomesDbContext db = CreateDb(_migratedFor);
        await db.Database.EnsureDeletedAsync();
        _migratedFor = null;
    }

    [RefundRacePostgresFact]
    public async Task Completion_race_with_an_unrelated_writer_is_never_reported_as_success()
    {
        string connectionString = await EnsureMigratedAsync();
        Guid bookingId = await SeedPaidCancelledBookingAsync(connectionString);

        // An unrelated writer (here a manual operator reconciliation with a
        // different reference) commits while the processor's readback is in
        // flight. The processor's completion write must lose, re-read the
        // committed row, see that it is NOT its own reference, and surface the
        // race — never report success for a completion it did not commit.
        var paymob = new RaceInjectingPaymob(connectionString, async competitor =>
        {
            CareHomeBooking current = await competitor.Bookings.SingleAsync(x => x.Id == bookingId);
            current.MarkRefundManuallyCompleted("manual-ref", "operator reconciled", UserId.New(), Now.AddMinutes(4));
            await competitor.SaveChangesAsync();
        });

        await using var db = CreateDb(connectionString);
        CareHomeBooking booking = await db.Bookings.SingleAsync(x => x.Id == bookingId);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => CareHomeRefundProcessor.InitiateAsync(db, paymob, booking, Now.AddMinutes(3), default));

        Assert.Equal(1, paymob.RefundCalls); // never a repeat refund POST
        Assert.Equal(1, paymob.VerifyCalls);

        await using var probe = CreateDb(connectionString);
        CareHomeBooking persisted = await probe.Bookings.AsNoTracking().SingleAsync(x => x.Id == bookingId);
        Assert.Equal(CareHomeRefundStatus.ManuallyCompleted, persisted.RefundStatus);
        Assert.Equal("manual-ref", persisted.RefundReference);
    }

    [RefundRacePostgresFact]
    public async Task Completion_race_against_the_same_reference_is_accepted_exactly_once()
    {
        string connectionString = await EnsureMigratedAsync();
        Guid bookingId = await SeedPaidCancelledBookingAsync(connectionString);

        // A concurrent worker (another verified readback, or the callback)
        // completes the SAME refund reference while the processor's completion
        // is in flight. The processor's write loses, re-reads, finds the
        // committed row completed against ITS reference, and accepts it — with
        // exactly one RefundCompleted event in total.
        var paymob = new RaceInjectingPaymob(connectionString, async competitor =>
        {
            CareHomeBooking current = await competitor.Bookings.SingleAsync(x => x.Id == bookingId);
            Assert.True(current.MarkRefundCompleted(RefundTransactionId.ToString(), Now.AddMinutes(4)));
            CareHomeNotificationEvents.Enqueue(
                competitor, CareHomeNotificationEvent.RefundCompleted, bookingId, Now.AddMinutes(4));
            await competitor.SaveChangesAsync();
        });

        await using var db = CreateDb(connectionString);
        CareHomeBooking booking = await db.Bookings.SingleAsync(x => x.Id == bookingId);

        Result initiated = await CareHomeRefundProcessor.InitiateAsync(
            db, paymob, booking, Now.AddMinutes(3), default);

        Assert.True(initiated.IsSuccess);
        Assert.Equal(1, paymob.RefundCalls);
        Assert.Equal(1, paymob.VerifyCalls);

        await using var probe = CreateDb(connectionString);
        CareHomeBooking persisted = await probe.Bookings.AsNoTracking().SingleAsync(x => x.Id == bookingId);
        Assert.Equal(CareHomeRefundStatus.Completed, persisted.RefundStatus);
        Assert.Equal(RefundTransactionId.ToString(), persisted.RefundReference);
        Assert.Single(probe.BookingNotificationOutbox,
            x => x.BookingId == bookingId && x.EventType == CareHomeNotificationEvent.RefundCompleted);
    }

    [RefundRacePostgresFact]
    public async Task Parent_payment_callback_race_accepts_the_commit_via_the_recorded_payment_id()
    {
        // The callback completion raced a concurrent writer of the SAME booking
        // (the processor's verified readback, or a duplicate delivery). This
        // delivery arrived on the refunded-PARENT path: its incoming id is the
        // capture (the booking's PaymobTransactionId), which can never equal the
        // stored child RefundReference. The re-read must accept the committed row
        // through the recorded payment id, so a genuinely applied callback is
        // acknowledged instead of spuriously refused.
        string connectionString = await EnsureMigratedAsync();
        Guid bookingId = await SeedInitiatedRefundBookingAsync(connectionString);

        await using var wrapper = new RacingSaveCareHomesDbContext(
            CreateDb(connectionString),
            async () =>
            {
                await using CareHomesDbContext competitor = CreateDb(connectionString);
                CareHomeBooking current = await competitor.Bookings.SingleAsync(x => x.Id == bookingId);
                Assert.True(current.MarkRefundCompleted(RefundTransactionId.ToString(), Now.AddMinutes(4)));
                CareHomeNotificationEvents.Enqueue(
                    competitor, CareHomeNotificationEvent.RefundCompleted, bookingId, Now.AddMinutes(4));
                await competitor.SaveChangesAsync();
            });

        Result result = await new CompleteCareHomeRefundCallbackHandler(wrapper).Handle(
            new CompleteCareHomeRefundCallbackCommand(
                CaptureTransactionId, true, false, true, AmountCents, MerchantOrderId, Now.AddMinutes(5)),
            default);

        Assert.True(result.IsSuccess);

        await using var probe = CreateDb(connectionString);
        CareHomeBooking persisted = await probe.Bookings.AsNoTracking().SingleAsync(x => x.Id == bookingId);
        Assert.Equal(CareHomeRefundStatus.Completed, persisted.RefundStatus);
        // The child reference is retained: the parent path never rewrote it.
        Assert.Equal(RefundTransactionId.ToString(), persisted.RefundReference);
        Assert.Equal(CaptureTransactionId, persisted.PaymobTransactionId);
        Assert.Single(probe.BookingNotificationOutbox,
            x => x.BookingId == bookingId && x.EventType == CareHomeNotificationEvent.RefundCompleted);
    }

    [RefundRacePostgresFact]
    public async Task Child_callback_race_refuses_a_completion_that_is_not_its_own_reference()
    {
        // The generic reference identity is NOT weakened: a child callback whose
        // own id is the stored refund reference must not be credited with a
        // concurrent completion that landed against a DIFFERENT reference (here
        // an operator manual completion). The race must surface, not be
        // acknowledged as this callback's completion.
        string connectionString = await EnsureMigratedAsync();
        Guid bookingId = await SeedInitiatedRefundBookingAsync(connectionString);

        await using var wrapper = new RacingSaveCareHomesDbContext(
            CreateDb(connectionString),
            async () =>
            {
                await using CareHomesDbContext competitor = CreateDb(connectionString);
                CareHomeBooking current = await competitor.Bookings.SingleAsync(x => x.Id == bookingId);
                current.MarkRefundManuallyCompleted("manual-ref", "operator reconciled", UserId.New(), Now.AddMinutes(4));
                await competitor.SaveChangesAsync();
            });

        Result result = await new CompleteCareHomeRefundCallbackHandler(wrapper).Handle(
            new CompleteCareHomeRefundCallbackCommand(
                RefundTransactionId, true, false, false, AmountCents, MerchantOrderId, Now.AddMinutes(5)),
            default);

        Assert.True(result.IsFailure);
        Assert.Equal("CareHomes.Refund.CallbackConcurrency", result.Error.Code);

        await using var probe = CreateDb(connectionString);
        CareHomeBooking persisted = await probe.Bookings.AsNoTracking().SingleAsync(x => x.Id == bookingId);
        Assert.Equal(CareHomeRefundStatus.ManuallyCompleted, persisted.RefundStatus);
        Assert.Equal("manual-ref", persisted.RefundReference);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static async Task<Guid> SeedInitiatedRefundBookingAsync(string connectionString)
    {
        await using CareHomesDbContext db = CreateDb(connectionString);
        CareHomeFacility facility = CareHomeFacility.CreateDraft(UserId.New(), Now);
        CareHomeBooking booking = CareHomeBooking.Create(
            facility.Id, UserId.New(), FamilyId.New(), ElderlyId.New(), Guid.NewGuid(),
            new DateOnly(2026, 10, 5), 10000m, 1000m, 500m, 4, "ar", "Elderly", 75, null,
            "Contact", "+201000000000", "Daughter", null, Now);
        booking.SetPaymentIntent(
            1, MerchantOrderId.ToString(), MerchantOrderId.ToString(), "client-secret", "public-key", Now);
        booking.MarkPaid(CaptureTransactionId, Now.AddMinutes(1));
        booking.CancelByFamily(Now.AddMinutes(2));
        // The processor has persisted the child refund reference: RefundStatus is
        // Initiated and the child's own id is the stored RefundReference, while
        // PaymobTransactionId remains the capture.
        Assert.True(booking.TryClaimRefund(Now.AddMinutes(3)));
        booking.MarkRefundInitiated(RefundTransactionId.ToString(), Now.AddMinutes(3));

        db.Facilities.Add(facility);
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        return booking.Id;
    }

    private static async Task<Guid> SeedPaidCancelledBookingAsync(string connectionString)
    {
        await using CareHomesDbContext db = CreateDb(connectionString);
        CareHomeFacility facility = CareHomeFacility.CreateDraft(UserId.New(), Now);
        CareHomeBooking booking = CareHomeBooking.Create(
            facility.Id, UserId.New(), FamilyId.New(), ElderlyId.New(), Guid.NewGuid(),
            new DateOnly(2026, 10, 5), 10000m, 1000m, 500m, 4, "ar", "Elderly", 75, null,
            "Contact", "+201000000000", "Daughter", null, Now);
        booking.SetPaymentIntent(
            1, MerchantOrderId.ToString(), MerchantOrderId.ToString(), "client-secret", "public-key", Now);
        booking.MarkPaid(CaptureTransactionId, Now.AddMinutes(1));
        booking.CancelByFamily(Now.AddMinutes(2));

        db.Facilities.Add(facility);
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        return booking.Id;
    }

    private async Task<string> EnsureMigratedAsync()
    {
        if (_migratedFor is not null)
        {
            return _migratedFor;
        }

        await MigrationLock.WaitAsync();
        try
        {
            if (_migratedFor is not null)
            {
                return _migratedFor;
            }

            string connectionString = GetValidatedConnectionString();
            await EnsureFreshDatabaseAbsentAsync(connectionString);

            await using (CareHomesDbContext db = CreateDb(connectionString))
            {
                await db.Database.MigrateAsync();
            }

            _migratedFor = connectionString;
            return connectionString;
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
            throw new InvalidOperationException($"The refund-race proof requires {ConnectionStringVariable}.");
        }

        var builder = new NpgsqlConnectionStringBuilder(raw);
        if (builder.Host is not ("localhost" or "127.0.0.1" or "::1") || builder.Port != 5432)
        {
            throw new InvalidOperationException(
                "Refusing to write: the refund-race proof source must be localhost:5432 PostgreSQL.");
        }

        builder.Database = ExpectedDatabase;
        return builder.ConnectionString;
    }

    private static async Task EnsureFreshDatabaseAbsentAsync(string connectionString)
    {
        var maintenance = new NpgsqlConnectionStringBuilder(connectionString) { Database = "postgres" };

        await using var connection = new NpgsqlConnection(maintenance.ConnectionString);
        await connection.OpenAsync();

        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS (SELECT 1 FROM pg_database WHERE datname = @database)";
        command.Parameters.AddWithValue("database", ExpectedDatabase);
        bool exists = (bool)(await command.ExecuteScalarAsync() ?? false);

        if (exists)
        {
            throw new InvalidOperationException("Refusing to write: the refund-race target already exists.");
        }
    }

    private static CareHomesDbContext CreateDb(string connectionString) =>
        new(new DbContextOptionsBuilder<CareHomesDbContext>()
            .UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", CareHomesDbContext.Schema))
            .Options);

    private static PaymobRefundVerification SettledView() =>
        new(RefundTransactionId, true, true, false, AmountCents, "EGP", MerchantOrderId, CaptureTransactionId);

    /// <summary>
    /// A gateway double whose refund call returns the exact provider refund id,
    /// and whose authenticated readback fires a competing writer (on its own
    /// context, exactly as a racing request would) before returning the settled
    /// view — so the processor's completion write races a real committed change.
    /// </summary>
    private sealed class RaceInjectingPaymob(
        string connectionString,
        Func<CareHomesDbContext, Task> compete) : IPaymobClient
    {
        public int RefundCalls { get; private set; }

        public int VerifyCalls { get; private set; }

        public Task<Result<PaymobPaymentIntent>> CreatePaymentIntentAsync(
            PaymobPaymentIntentInput input,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<PaymobPaymentIntent>.Failure(
                new Error("Paymob.NotUsed", "Payment intent is not used by this test.")));

        public Task<Result<string?>> RefundPaymentAsync(
            string paymobTransactionId,
            decimal amount,
            CancellationToken cancellationToken = default)
        {
            RefundCalls++;
            return Task.FromResult(Result<string?>.Success(RefundTransactionId.ToString()));
        }

        public async Task<Result<PaymobRefundVerification>> VerifyRefundTransactionAsync(
            string refundTransactionId,
            CancellationToken cancellationToken = default)
        {
            VerifyCalls++;
            await using CareHomesDbContext competitor = CreateDb(connectionString);
            await compete(competitor);
            return Result<PaymobRefundVerification>.Success(SettledView());
        }
    }

    private sealed class RacingSaveCareHomesDbContext(CareHomesDbContext inner, Func<Task> race) : ICareHomesDbContext, IAsyncDisposable
    {
        private bool _raced;

        public DbSet<CareHomeFacility> Facilities => inner.Facilities;
        public DbSet<CareHomeRoomType> RoomTypes => inner.RoomTypes;
        public DbSet<CareHomeRoomTypeMedia> RoomTypeMedia => inner.RoomTypeMedia;
        public DbSet<CareHomeRoom> Rooms => inner.Rooms;
        public DbSet<CareHomeRoomMedia> RoomMedia => inner.RoomMedia;
        public DbSet<CareHomeBed> Beds => inner.Beds;
        public DbSet<CareHomeMaintenanceBlock> MaintenanceBlocks => inner.MaintenanceBlocks;
        public DbSet<CareHomeBooking> Bookings => inner.Bookings;
        public DbSet<CareHomeInternalBookingNote> InternalBookingNotes => inner.InternalBookingNotes;
        public DbSet<CareHomeRating> Ratings => inner.Ratings;
        public DbSet<CareHomeRatingModerationAction> RatingModerationActions => inner.RatingModerationActions;
        public DbSet<CareHomeRatingFlag> RatingFlags => inner.RatingFlags;
        public DbSet<CareHomePayout> Payouts => inner.Payouts;
        public DbSet<CareHomePayoutDebt> PayoutDebts => inner.PayoutDebts;
        public DbSet<CareHomeBookingAssignmentHistory> BookingAssignmentHistory => inner.BookingAssignmentHistory;
        public DbSet<CareHomeTransferNotificationOutbox> TransferNotificationOutbox => inner.TransferNotificationOutbox;
        public DbSet<CareHomeNotificationOutbox> BookingNotificationOutbox => inner.BookingNotificationOutbox;
        public DbSet<CareHomeCheckInDispute> CheckInDisputes => inner.CheckInDisputes;
        public DbSet<CareHomeProfileMedia> ProfileMedia => inner.ProfileMedia;
        public DbSet<CareHomeVisitSettings> VisitSettings => inner.VisitSettings;
        public DbSet<CareHomeVisit> Visits => inner.Visits;
        public DbSet<IdempotencyRequest> IdempotencyRequests => inner.IdempotencyRequests;

        public void DetachAdded<TEntity>() where TEntity : class => inner.DetachAdded<TEntity>();

        public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (!_raced)
            {
                // A competing writer commits the same booking on its own context
                // exactly as a racing request would; the wrapped save then hits
                // the real concurrency token and throws DbUpdateConcurrencyException.
                _raced = true;
                await race();
            }

            return await inner.SaveChangesAsync(cancellationToken);
        }

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    private sealed class RefundRacePostgresFactAttribute : FactAttribute
    {
        public RefundRacePostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionStringVariable)))
            {
                Skip = "Refund-race PostgreSQL proof is skipped. Set " +
                       ConnectionStringVariable +
                       " to a local PostgreSQL instance to run it against a uniquely named disposable target.";
            }
        }
    }
}
