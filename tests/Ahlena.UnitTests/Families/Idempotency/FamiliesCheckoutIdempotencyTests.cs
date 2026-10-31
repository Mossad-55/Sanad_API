using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.Idempotency;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Enums;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.BuildingBlocks.Domain.ValueObjects;
using Ahlena.Modules.Families.Application.Abstractions.Caregivers;
using Ahlena.Modules.Families.Application.Abstractions.Data;
using Ahlena.Modules.Families.Application.Bookings;
using Ahlena.Modules.Families.Domain.Bookings;
using Ahlena.Modules.Families.Domain.Elderlies;
using Ahlena.Modules.Families.Domain.Families;
using Ahlena.Modules.Families.Infrastructure.Persistence;
using Ahlena.UnitTests.Finance;
using Ahlena.UnitTests.Support;
using IdempotencyRecord = Ahlena.Modules.Families.Domain.Idempotency.IdempotencyRequest;

namespace Ahlena.UnitTests.Families.Idempotency;

/// <summary>
/// Families booking checkout retry safety: an absent header behaves exactly as
/// today, a confirmed retry replays the stored status/body with no second
/// booking, a changed request fingerprint is a 409, and a concurrent duplicate
/// loses on the unique index, detaches its rows and replays the winner.
/// </summary>
public sealed class FamiliesCheckoutIdempotencyTests
{
    private static readonly DateTime UtcNow = new(2026, 10, 25, 9, 0, 0, DateTimeKind.Utc);
    private const string Operation = IdempotencyOperations.FamilyBookingCheckout;

    private sealed class FakePricing : ICaregiverBookingPricing
    {
        public Task<Result<CaregiverBookingPrice>> GetBookingPriceAsync(
            CaregiverId caregiverId,
            BookingShiftType shiftType,
            TimeOnly startTime,
            TimeOnly endTime,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<CaregiverBookingPrice>.Success(
                new CaregiverBookingPrice(BookingCaregiverType.Medical, 500m)));
    }

    private static FamiliesDbContext CreateInMemoryDb() =>
        new(new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static (FamiliesDbContext Db, Family Family, Elderly Elderly) SeedInMemory()
    {
        var db = CreateInMemoryDb();
        var (family, elderly) = AddFamily(db);
        db.SaveChanges();
        return (db, family, elderly);
    }

    private static (Family Family, Elderly Elderly) AddFamily(FamiliesDbContext db)
    {
        var family = Family.Create(UserId.New(), "Idempotency Family");
        var elderly = Elderly.Create(
            family.OwnerUserId,
            UserId.New(),
            family.Id,
            FamilyRelationshipType.Father,
            FullName.Create("مسن"),
            FullName.Create("Elderly"),
            Gender.Male,
            new DateOnly(1950, 1, 1),
            DateOnly.FromDateTime(UtcNow));

        db.Families.Add(family);
        db.Elderlies.Add(elderly);

        return (family, elderly);
    }

    private static CreateBookingCheckoutCommandHandler Handler(
        IFamiliesDbContext db,
        bool isAvailable = true) =>
        new(
            db,
            new FakePricing(),
            new FixedPlatformChargeRules(15m, 0m),
            FixedPlatformControls.Operational(),
            new StubIdempotentResponseSnapshotter(),
            new StubCaregiverAvailabilityRules(isAvailable),
            new PassThroughCaregiverBookingCheckoutGuard());

    private static CreateBookingCheckoutCommand Command(
        Family family,
        Elderly elderly,
        IdempotencyRequest? idempotency = null) =>
        new(
            family.OwnerUserId,
            elderly.Id,
            new CaregiverId(Guid.Parse("11111111-2222-3333-4444-555555555555")),
            BookingShiftType.HomeVisit,
            new DateOnly(2026, 10, 27),
            new TimeOnly(10, 0),
            new TimeOnly(12, 0),
            "12 Nile St, Cairo",
            "Leave the key with the doorman",
            DateOnly.FromDateTime(UtcNow),
            UtcNow,
            Idempotency: idempotency);

    private static IdempotencyRequest Request(string key, string fingerprint) =>
        new(new IdempotencyKey(key), fingerprint);

    [Fact]
    public async Task FirstCall_WithAKey_StoresOneSnapshotRowAtomicallyWithTheBooking()
    {
        var (db, family, elderly) = SeedInMemory();
        var request = Request("checkout-key-1", "fingerprint-a");

        var result = await Handler(db).Handle(
            Command(family, elderly, request),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsReplay);
        Assert.Equal(200, result.Value.StatusCode);
        Assert.NotEqual(Guid.Empty, result.Value.Value.BookingId);
        Assert.Single(db.Bookings);

        IdempotencyRecord stored = Assert.Single(db.IdempotencyRequests);
        Assert.Equal(family.OwnerUserId, stored.ActorId);
        Assert.Equal(Operation, stored.Operation);
        Assert.Equal("checkout-key-1", stored.Key);
        Assert.Equal("fingerprint-a", stored.Fingerprint);
        Assert.Equal((Guid?)result.Value.Value.BookingId, stored.ResourceId);
        Assert.Equal(200, stored.ResponseStatus);
        Assert.Equal(
            new StubIdempotentResponseSnapshotter().Snapshot(result.Value.Value),
            stored.ResponseBody);
    }

    [Fact]
    public async Task Retry_WithTheSameKeyAndFingerprint_ReplaysWithoutASecondBooking()
    {
        var (db, family, elderly) = SeedInMemory();
        var request = Request("checkout-key-2", "fingerprint-b");
        var handler = Handler(db);

        var first = await handler.Handle(Command(family, elderly, request), CancellationToken.None);
        Assert.True(first.IsSuccess);

        var second = await handler.Handle(Command(family, elderly, request), CancellationToken.None);

        Assert.True(second.IsSuccess);
        Assert.True(second.Value.IsReplay);
        Assert.Equal(200, second.Value.StatusCode);
        Assert.Equal(Assert.Single(db.IdempotencyRequests).ResponseBody, second.Value.ReplayBody);
        Assert.Single(db.Bookings);
    }

    [Fact]
    public async Task Retry_WithAChangedFingerprint_IsA409()
    {
        var (db, family, elderly) = SeedInMemory();
        var handler = Handler(db);

        var first = await handler.Handle(
            Command(family, elderly, Request("checkout-key-3", "fingerprint-c")),
            CancellationToken.None);
        Assert.True(first.IsSuccess);

        var second = await handler.Handle(
            Command(family, elderly, Request("checkout-key-3", "fingerprint-different")),
            CancellationToken.None);

        Assert.False(second.IsSuccess);
        Assert.Equal("Bookings.IdempotencyConflict", second.Error.Code);
        Assert.Single(db.Bookings);
        Assert.Single(db.IdempotencyRequests);
    }

    [Fact]
    public async Task AbsentHeader_KeepsTodaysBehaviorAndStoresNoKey()
    {
        var (db, family, elderly) = SeedInMemory();

        var result = await Handler(db).Handle(
            Command(family, elderly, idempotency: null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsReplay);
        Assert.Single(db.Bookings);
        Assert.Empty(db.IdempotencyRequests);
    }

    /// <summary>
    /// Simulates the real race: another request commits the unique
    /// <c>(actor, operation, key)</c> row between this request's lookup and its
    /// own save. The loser must detach its booking and replay the winner
    /// instead of surfacing a 500 or persisting a duplicate.
    /// </summary>
    [Fact]
    public async Task ConcurrentDuplicate_DetachesTheLoserAndReplaysTheWinner()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new FamiliesDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var (family, elderly) = AddFamily(db);
        await db.SaveChangesAsync();

        const string key = "checkout-race-key";
        const string fingerprint = "race-fingerprint";
        const string winnerBody = "{\"BookingId\":\"winner-booking\",\"Status\":0}";

        IdempotencyRecord winner = IdempotencyRecord.Create(
            family.OwnerUserId,
            Operation,
            key,
            fingerprint,
            Guid.NewGuid(),
            200,
            winnerBody,
            UtcNow);

        var racing = new WinnerCommitsDuringSaveDbContext(db, options, winner);

        var result = await Handler(racing).Handle(
            Command(family, elderly, Request(key, fingerprint)),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsReplay);
        Assert.Equal(winnerBody, result.Value.ReplayBody);
        Assert.Equal(200, result.Value.StatusCode);

        Assert.Empty(await db.Bookings.ToListAsync());
        IdempotencyRecord stored = Assert.Single(await db.IdempotencyRequests.ToListAsync());
        Assert.Equal(key, stored.Key);
    }

    /// <summary>
    /// Delegates to the real context, but commits a competing winner row just
    /// before the loser's save, exactly as a concurrent request would.
    /// </summary>
    private sealed class WinnerCommitsDuringSaveDbContext(
        FamiliesDbContext inner,
        DbContextOptions<FamiliesDbContext> options,
        IdempotencyRecord winner)
        : FamiliesDbContextAdapter(inner)
    {
        private bool _winnerCommitted;

        public override async Task<int> SaveChangesAsync(
            CancellationToken cancellationToken = default)
        {
            if (!_winnerCommitted)
            {
                _winnerCommitted = true;

                await using var winnerContext = new FamiliesDbContext(options);
                winnerContext.IdempotencyRequests.Add(winner);
                await winnerContext.SaveChangesAsync(cancellationToken);
            }

            return await base.SaveChangesAsync(cancellationToken);
        }
    }
}
