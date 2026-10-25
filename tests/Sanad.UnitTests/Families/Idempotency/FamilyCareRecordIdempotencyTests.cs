using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.Idempotency;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Families.Application.CareRecords;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Domain.CareRecords;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Infrastructure.Persistence;
using Sanad.UnitTests.Support;
using IdempotencyRecord = Sanad.Modules.Families.Domain.Idempotency.IdempotencyRequest;

namespace Sanad.UnitTests.Families.Idempotency;

/// <summary>
/// Both care-record creates are retry-safe: a confirmed retry replays the
/// stored 201 body without a second record or a second audit row, a changed
/// fingerprint is a 409, and an absent header keeps today's behavior.
/// </summary>
public sealed class FamilyCareRecordIdempotencyTests
{
    private static readonly DateTime UtcNow = new(2026, 10, 25, 10, 0, 0, DateTimeKind.Utc);

    private sealed class FixedClock : IDateTimeProvider
    {
        public DateTime UtcNow => FamilyCareRecordIdempotencyTests.UtcNow;
    }

    private static FamiliesDbContext CreateInMemoryDb() =>
        new(new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static (FamiliesDbContext Db, Family Family, Elderly Elderly) SeedFamily()
    {
        var db = CreateInMemoryDb();
        var family = Family.Create(UserId.New(), "Care Record Idempotency Family");
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
        db.SaveChanges();

        return (db, family, elderly);
    }

    private static CaregiverId AddConfirmedBooking(
        FamiliesDbContext db,
        Family family,
        Elderly elderly)
    {
        var caregiverId = CaregiverId.New();
        var booking = Booking.Create(
            family.Id,
            family.OwnerUserId,
            elderly.Id,
            caregiverId,
            BookingCaregiverType.Medical,
            BookingShiftType.HomeVisit,
            DateOnly.FromDateTime(UtcNow).AddDays(2),
            new TimeOnly(10, 0),
            new TimeOnly(12, 0),
            "123 Nile St, Cairo",
            null,
            BookingPriceSnapshot.Calculate(500m, 15m),
            UtcNow.AddHours(24),
            DateOnly.FromDateTime(UtcNow),
            UtcNow);

        booking.MarkAsPaid(booking.Id.Value.ToString(), $"txn-{Guid.NewGuid():N}", UtcNow);
        booking.AcceptByCaregiver(UtcNow.AddMinutes(1));
        db.Bookings.Add(booking);
        db.SaveChanges();

        return caregiverId;
    }

    private static IdempotencyRequest Request(string key, string fingerprint) =>
        new(new IdempotencyKey(key), fingerprint);

    private static CreateFamilyCareRecordCommand FamilyCommand(
        Family family,
        Elderly elderly,
        IdempotencyRequest? idempotency) =>
        new(
            family.OwnerUserId,
            elderly.Id,
            "Cairo Hospital",
            new DateOnly(2026, 10, 20),
            "Follow-up visit",
            "corr-1",
            idempotency);

    private static CreateCaregiverCareRecordCommand CaregiverCommand(
        CaregiverId caregiverId,
        Family family,
        Elderly elderly,
        IdempotencyRequest? idempotency) =>
        new(
            caregiverId,
            family.OwnerUserId,
            elderly.Id,
            "Cairo Hospital",
            new DateOnly(2026, 10, 20),
            "Follow-up visit",
            "corr-2",
            idempotency);

    [Fact]
    public async Task FamilyCreate_WithAKey_StoresTheSnapshotWithTheRecordAndAudit()
    {
        var (db, family, elderly) = SeedFamily();
        var handler = new CreateFamilyCareRecordCommandHandler(
            db,
            new FixedClock(),
            new StubIdempotentResponseSnapshotter());

        var result = await handler.Handle(
            FamilyCommand(
                family,
                elderly,
                Request("family-cr-1", "family-fp-1")),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsReplay);
        Assert.Equal(201, result.Value.StatusCode);
        Assert.Single(db.CareRecords);
        Assert.Single(db.CareRecordAudits);

        IdempotencyRecord stored = Assert.Single(db.IdempotencyRequests);
        Assert.Equal(family.OwnerUserId, stored.ActorId);
        Assert.Equal(IdempotencyOperations.FamilyCareRecordCreate, stored.Operation);
        Assert.Equal("family-cr-1", stored.Key);
        Assert.Equal("family-fp-1", stored.Fingerprint);
        Assert.Equal((Guid?)result.Value.Value.Id, stored.ResourceId);
        Assert.Equal(201, stored.ResponseStatus);
        Assert.Equal(
            new StubIdempotentResponseSnapshotter().Snapshot(result.Value.Value),
            stored.ResponseBody);
    }

    [Fact]
    public async Task FamilyCreate_Retry_ReplaysWithoutASecondRecordOrAudit()
    {
        var (db, family, elderly) = SeedFamily();
        var handler = new CreateFamilyCareRecordCommandHandler(
            db,
            new FixedClock(),
            new StubIdempotentResponseSnapshotter());
        var request = Request("family-cr-2", "family-fp-2");

        var first = await handler.Handle(
            FamilyCommand(family, elderly, request),
            CancellationToken.None);
        Assert.True(first.IsSuccess);

        var second = await handler.Handle(
            FamilyCommand(family, elderly, request),
            CancellationToken.None);

        Assert.True(second.IsSuccess);
        Assert.True(second.Value.IsReplay);
        Assert.Equal(201, second.Value.StatusCode);
        Assert.Equal(Assert.Single(db.IdempotencyRequests).ResponseBody, second.Value.ReplayBody);
        Assert.Single(db.CareRecords);
        Assert.Single(db.CareRecordAudits);
    }

    [Fact]
    public async Task FamilyCreate_ChangedFingerprint_IsA409()
    {
        var (db, family, elderly) = SeedFamily();
        var handler = new CreateFamilyCareRecordCommandHandler(
            db,
            new FixedClock(),
            new StubIdempotentResponseSnapshotter());

        var first = await handler.Handle(
            FamilyCommand(family, elderly, Request("family-cr-3", "family-fp-3")),
            CancellationToken.None);
        Assert.True(first.IsSuccess);

        var second = await handler.Handle(
            FamilyCommand(family, elderly, Request("family-cr-3", "family-fp-different")),
            CancellationToken.None);

        Assert.False(second.IsSuccess);
        Assert.Equal("Families.CareRecords.IdempotencyConflict", second.Error.Code);
        Assert.Single(db.CareRecords);
        Assert.Single(db.CareRecordAudits);
    }

    [Fact]
    public async Task FamilyCreate_AbsentHeader_StoresNoKey()
    {
        var (db, family, elderly) = SeedFamily();
        var handler = new CreateFamilyCareRecordCommandHandler(
            db,
            new FixedClock(),
            new StubIdempotentResponseSnapshotter());

        var result = await handler.Handle(
            FamilyCommand(family, elderly, idempotency: null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsReplay);
        Assert.Single(db.CareRecords);
        Assert.Empty(db.IdempotencyRequests);
    }

    [Fact]
    public async Task CaregiverCreate_Retry_ReplaysWithoutASecondRecordOrAudit()
    {
        var (db, family, elderly) = SeedFamily();
        var caregiverId = AddConfirmedBooking(db, family, elderly);
        var handler = new CreateCaregiverCareRecordCommandHandler(
            db,
            new FixedClock(),
            new StubIdempotentResponseSnapshotter());
        var request = Request("caregiver-cr-1", "caregiver-fp-1");

        var first = await handler.Handle(
            CaregiverCommand(caregiverId, family, elderly, request),
            CancellationToken.None);
        Assert.True(first.IsSuccess);
        Assert.Equal(201, first.Value.StatusCode);

        IdempotencyRecord stored = Assert.Single(db.IdempotencyRequests);
        Assert.Equal(IdempotencyOperations.CaregiverCareRecordCreate, stored.Operation);
        Assert.Equal("caregiver-fp-1", stored.Fingerprint);

        var second = await handler.Handle(
            CaregiverCommand(caregiverId, family, elderly, request),
            CancellationToken.None);

        Assert.True(second.IsSuccess);
        Assert.True(second.Value.IsReplay);
        Assert.Equal(201, second.Value.StatusCode);
        Assert.Equal(stored.ResponseBody, second.Value.ReplayBody);
        Assert.Single(db.CareRecords);
        Assert.Single(db.CareRecordAudits);
    }

    [Fact]
    public async Task CaregiverCreate_ChangedFingerprint_IsA409()
    {
        var (db, family, elderly) = SeedFamily();
        var caregiverId = AddConfirmedBooking(db, family, elderly);
        var handler = new CreateCaregiverCareRecordCommandHandler(
            db,
            new FixedClock(),
            new StubIdempotentResponseSnapshotter());

        var first = await handler.Handle(
            CaregiverCommand(caregiverId, family, elderly, Request("caregiver-cr-2", "caregiver-fp-2")),
            CancellationToken.None);
        Assert.True(first.IsSuccess);

        var second = await handler.Handle(
            CaregiverCommand(caregiverId, family, elderly, Request("caregiver-cr-2", "caregiver-fp-different")),
            CancellationToken.None);

        Assert.False(second.IsSuccess);
        Assert.Equal("Families.CareRecords.IdempotencyConflict", second.Error.Code);
        Assert.Single(db.CareRecords);
        Assert.Single(db.CareRecordAudits);
    }
}
