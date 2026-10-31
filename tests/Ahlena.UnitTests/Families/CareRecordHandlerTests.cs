using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Ahlena.BuildingBlocks.Application.Abstractions;
using Ahlena.BuildingBlocks.Domain.Enums;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.BuildingBlocks.Domain.ValueObjects;
using Ahlena.Modules.Families.Application.CareRecords;
using Ahlena.Modules.Families.Domain.Bookings;
using Ahlena.Modules.Families.Domain.CareRecords;
using Ahlena.Modules.Families.Domain.Elderlies;
using Ahlena.Modules.Families.Domain.Families;
using Ahlena.Modules.Families.Infrastructure.Persistence;
using Ahlena.UnitTests.Support;

namespace Ahlena.UnitTests.Families;

/// <summary>
/// W1 care-record handler authorization matrix: the family role/source gates,
/// the caregiver Confirmed/InProgress + 30-day completion-grace window with
/// its revalidation between the picker and create, the elderly self-scoped
/// read-only timeline, the care-home facility attribution scope, admin
/// read-only payload-free audits, the shared paging/filter/search contract and
/// the version-based 409 path.
/// </summary>
public sealed class CareRecordHandlerTests
{
    private static readonly DateTime UtcNow = new(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);

    private sealed class FixedClock : IDateTimeProvider
    {
        public DateTime UtcNow { get; } = CareRecordHandlerTests.UtcNow;
    }

    private static FamiliesDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static (FamiliesDbContext Db, Family Family, Elderly Elderly) SeedFamily()
    {
        var db = CreateDbContext();
        var family = Family.Create(UserId.New(), "Care Record Family");

        var elderly = Elderly.Create(
            family.OwnerUserId,
            UserId.New(),
            family.Id,
            FamilyRelationshipType.Father,
            FullName.Create("مسن تجريبي"),
            FullName.Create("Elderly Test"),
            Gender.Male,
            new DateOnly(1950, 1, 1),
            DateOnly.FromDateTime(UtcNow));

        db.Families.Add(family);
        db.Elderlies.Add(elderly);
        db.SaveChanges();

        return (db, family, elderly);
    }

    private static CaregiverId ConfirmedBooking(
        FamiliesDbContext db,
        Family family,
        Elderly elderly,
        DateTime now)
    {
        var caregiverId = CaregiverId.New();
        var booking = Booking.Create(
            family.Id,
            family.OwnerUserId,
            elderly.Id,
            caregiverId,
            BookingCaregiverType.Medical,
            BookingShiftType.HomeVisit,
            DateOnly.FromDateTime(now).AddDays(2),
            new TimeOnly(10, 0),
            new TimeOnly(12, 0),
            "123 Nile St, Cairo",
            null,
            BookingPriceSnapshot.Calculate(500m, 15m),
            now.AddHours(24),
            DateOnly.FromDateTime(now),
            now);

        booking.MarkAsPaid(booking.Id.Value.ToString(), $"txn-{Guid.NewGuid():N}", now);
        booking.AcceptByCaregiver(now.AddMinutes(1));
        db.Bookings.Add(booking);
        db.SaveChanges();

        return caregiverId;
    }

    // ============================ Family surface ============================

    [Fact]
    public async Task FamilyList_CombinesAllThreeSources_OwnedOnly()
    {
        var (db, family, elderly) = SeedFamily();
        db.CareRecords.AddRange(
            CareRecord.CreateForFamily(elderly.Id, family.OwnerUserId, family.Id, "A", new(2026, 10, 1), "d", UtcNow),
            CareRecord.CreateForCaregiver(elderly.Id, UserId.New(), CaregiverId.New(), "B", new(2026, 10, 2), "d", UtcNow),
            CareRecord.CreateForCareHome(elderly.Id, UserId.New(), Guid.NewGuid(), "C", new(2026, 10, 3), "d", UtcNow),
            // Another family's dependent must never appear.
            CareRecord.CreateForCaregiver(ElderlyId.New(), UserId.New(), CaregiverId.New(), "D", new(2026, 10, 4), "d", UtcNow));
        db.SaveChanges();

        var result = await new ListFamilyCareRecordsQueryHandler(db).Handle(
            new ListFamilyCareRecordsQuery(family.OwnerUserId, elderly.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.TotalCount);
        Assert.Equal(
            [new(2026, 10, 3), new(2026, 10, 2), new(2026, 10, 1)],
            result.Value.Items.Select(i => i.VisitDate).ToArray());
    }

    [Fact]
    public async Task FamilyList_ViewerRoleCanRead()
    {
        var (db, family, elderly) = SeedFamily();
        var viewer = UserId.New();
        family.AddMember(FamilyMember.Create(
            viewer, family.OwnerUserId, FamilyRelationshipType.Other, FamilyRole.Viewer));
        db.CareRecords.Add(CareRecord.CreateForFamily(
            elderly.Id, family.OwnerUserId, family.Id, "A", new(2026, 10, 1), "d", UtcNow));
        db.SaveChanges();

        var result = await new ListFamilyCareRecordsQueryHandler(db).Handle(
            new ListFamilyCareRecordsQuery(viewer, elderly.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Items);
    }

    [Fact]
    public async Task FamilyList_FiltersSearchSourceAndPaging()
    {
        var (db, family, elderly) = SeedFamily();
        db.CareRecords.AddRange(
            CareRecord.CreateForFamily(elderly.Id, family.OwnerUserId, family.Id, "Nile Hospital", new(2026, 10, 1), "d", UtcNow),
            CareRecord.CreateForCaregiver(elderly.Id, UserId.New(), CaregiverId.New(), "Nile Clinic", new(2026, 10, 2), "d", UtcNow),
            CareRecord.CreateForCareHome(elderly.Id, UserId.New(), Guid.NewGuid(), "Other Place", new(2026, 10, 3), "d", UtcNow));
        db.SaveChanges();

        var searched = await new ListFamilyCareRecordsQueryHandler(db).Handle(
            new ListFamilyCareRecordsQuery(family.OwnerUserId, elderly.Id, Search: "  NILE  "),
            CancellationToken.None);
        Assert.True(searched.IsSuccess);
        Assert.Equal(2, searched.Value.TotalCount); // search never touches details

        var source = await new ListFamilyCareRecordsQueryHandler(db).Handle(
            new ListFamilyCareRecordsQuery(family.OwnerUserId, elderly.Id, Source: CareRecordSource.Caregiver),
            CancellationToken.None);
        Assert.True(source.IsSuccess);
        Assert.Single(source.Value.Items);
        Assert.Equal(CareRecordSource.Caregiver, source.Value.Items[0].Source);

        var paged = await new ListFamilyCareRecordsQueryHandler(db).Handle(
            new ListFamilyCareRecordsQuery(family.OwnerUserId, elderly.Id, Page: 2, PageSize: 2),
            CancellationToken.None);
        Assert.True(paged.IsSuccess);
        Assert.Equal(3, paged.Value.TotalCount);
        Assert.Single(paged.Value.Items);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task FamilyList_RejectsBadPaging(int page, int pageSize)
    {
        var (db, family, elderly) = SeedFamily();

        var result = await new ListFamilyCareRecordsQueryHandler(db).Handle(
            new ListFamilyCareRecordsQuery(family.OwnerUserId, elderly.Id, Page: page, PageSize: pageSize),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CareRecordErrors.InvalidQuery, result.Error);
    }

    [Fact]
    public async Task FamilyList_RejectsReversedRangeOverlongSearchAndUnknownSource()
    {
        var (db, family, elderly) = SeedFamily();

        var reversed = await new ListFamilyCareRecordsQueryHandler(db).Handle(
            new ListFamilyCareRecordsQuery(family.OwnerUserId, elderly.Id,
                VisitDateFrom: new(2026, 10, 2), VisitDateTo: new(2026, 10, 1)),
            CancellationToken.None);
        Assert.Equal(CareRecordErrors.InvalidQuery, reversed.Error);

        var longSearch = await new ListFamilyCareRecordsQueryHandler(db).Handle(
            new ListFamilyCareRecordsQuery(family.OwnerUserId, elderly.Id, Search: new string('x', 101)),
            CancellationToken.None);
        Assert.Equal(CareRecordErrors.InvalidQuery, longSearch.Error);
    }

    [Fact]
    public async Task FamilyList_RejectsForeignDependentAndStranger()
    {
        var (db, _, elderly) = SeedFamily();
        // A second family inside the SAME database: its owner resolves fine but
        // the dependent does not belong to it.
        db.Families.Add(Family.Create(UserId.New(), "Foreign Family"));
        db.SaveChanges();
        var foreignOwner = await db.Families
            .Where(f => f.Name == "Foreign Family")
            .Select(f => f.OwnerUserId)
            .SingleAsync();

        var foreignDependent = await new ListFamilyCareRecordsQueryHandler(db).Handle(
            new ListFamilyCareRecordsQuery(foreignOwner, elderly.Id),
            CancellationToken.None);
        Assert.True(foreignDependent.IsFailure);
        Assert.Equal(CareRecordErrors.DependentNotFound, foreignDependent.Error);

        var stranger = await new ListFamilyCareRecordsQueryHandler(db).Handle(
            new ListFamilyCareRecordsQuery(UserId.New(), elderly.Id),
            CancellationToken.None);
        Assert.True(stranger.IsFailure);
        Assert.Equal(CareRecordErrors.AccessDenied, stranger.Error);
    }

    [Fact]
    public async Task FamilyCreate_OwnerSucceedsWithAuditRow_ViewerIsDenied()
    {
        var (db, family, elderly) = SeedFamily();
        var viewer = UserId.New();
        family.AddMember(FamilyMember.Create(
            viewer, family.OwnerUserId, FamilyRelationshipType.Other, FamilyRole.Viewer));
        db.SaveChanges();

        var create = await new CreateFamilyCareRecordCommandHandler(db, new FixedClock(), new StubIdempotentResponseSnapshotter()).Handle(
            new CreateFamilyCareRecordCommand(
                family.OwnerUserId, elderly.Id, "Local Hospital",
                new(2026, 10, 10), "checkup", "corr-1"),
            CancellationToken.None);

        Assert.True(create.IsSuccess);
        Assert.Equal(CareRecordSource.Family, create.Value.Value.Source);
        Assert.Equal(1, create.Value.Value.Version);

        var audit = await db.CareRecordAudits.SingleAsync();
        Assert.Equal(CareRecordAuditAction.Created, audit.Action);
        Assert.Equal(create.Value.Value.Id, audit.RecordId);
        Assert.Equal(CareRecordSource.Family, audit.Source);

        var denied = await new CreateFamilyCareRecordCommandHandler(db, new FixedClock(), new StubIdempotentResponseSnapshotter()).Handle(
            new CreateFamilyCareRecordCommand(
                viewer, elderly.Id, "Viewer Hospital",
                new(2026, 10, 11), "x", "corr-2"),
            CancellationToken.None);

        Assert.True(denied.IsFailure);
        Assert.Equal(CareRecordErrors.AccessDenied, denied.Error);
    }

    [Fact]
    public async Task FamilyCreate_OverlongDetailsIsInvalid_400()
    {
        var (db, family, elderly) = SeedFamily();

        var result = await new CreateFamilyCareRecordCommandHandler(db, new FixedClock(), new StubIdempotentResponseSnapshotter()).Handle(
            new CreateFamilyCareRecordCommand(
                family.OwnerUserId, elderly.Id, "Hospital",
                new(2026, 10, 10), new string('ط', 4001), "corr"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CareRecordErrors.Invalid, result.Error);
    }

    [Fact]
    public async Task FamilyUpdate_OwnSourceBumpsVersionAndAudits()
    {
        var (db, family, elderly) = SeedFamily();
        var record = CareRecord.CreateForFamily(
            elderly.Id, family.OwnerUserId, family.Id, "Old", new(2026, 10, 1), "old", UtcNow);
        db.CareRecords.Add(record);
        db.SaveChanges();

        var result = await new UpdateFamilyCareRecordCommandHandler(db, new FixedClock()).Handle(
            new UpdateFamilyCareRecordCommand(
                family.OwnerUserId, elderly.Id, record.Id,
                "New Hospital", new(2026, 10, 2), "new", record.Version, "corr"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Version);
        Assert.NotNull(result.Value.UpdatedOnUtc);
        Assert.Equal(UtcNow, result.Value.UpdatedOnUtc);
        Assert.Contains(
            await db.CareRecordAudits.SingleAsync(a => a.Action == CareRecordAuditAction.Updated),
            await db.CareRecordAudits.ToListAsync());
    }

    [Fact]
    public async Task FamilyUpdate_StaleVersionIs409()
    {
        var (db, family, elderly) = SeedFamily();
        var record = CareRecord.CreateForFamily(
            elderly.Id, family.OwnerUserId, family.Id, "Old", new(2026, 10, 1), "old", UtcNow);
        db.CareRecords.Add(record);
        db.SaveChanges();

        var result = await new UpdateFamilyCareRecordCommandHandler(db, new FixedClock()).Handle(
            new UpdateFamilyCareRecordCommand(
                family.OwnerUserId, elderly.Id, record.Id,
                "New Hospital", new(2026, 10, 2), "new", record.Version + 5, "corr"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CareRecordErrors.ConcurrencyConflict, result.Error);
    }

    [Fact]
    public async Task FamilyUpdateOrDelete_ForeignSourceUnderAuthorizedDependentIs403()
    {
        var (db, family, elderly) = SeedFamily();
        var record = CareRecord.CreateForCaregiver(
            elderly.Id, UserId.New(), CaregiverId.New(), "Caregiver Row", new(2026, 10, 1), "x", UtcNow);
        db.CareRecords.Add(record);
        db.SaveChanges();

        var update = await new UpdateFamilyCareRecordCommandHandler(db, new FixedClock()).Handle(
            new UpdateFamilyCareRecordCommand(
                family.OwnerUserId, elderly.Id, record.Id, "New", new(2026, 10, 2), "new", 1, "corr"),
            CancellationToken.None);
        Assert.Equal(CareRecordErrors.AccessDenied, update.Error);

        var delete = await new DeleteFamilyCareRecordCommandHandler(db, new FixedClock()).Handle(
            new DeleteFamilyCareRecordCommand(family.OwnerUserId, elderly.Id, record.Id, 1, "corr"),
            CancellationToken.None);
        Assert.Equal(CareRecordErrors.AccessDenied, delete.Error);
    }

    [Fact]
    public async Task FamilyUpdate_KnownIdOfOtherDependentIsNotFound()
    {
        var (db, family, elderly) = SeedFamily();
        var other = CareRecord.CreateForFamily(
            ElderlyId.New(), UserId.New(), FamilyId.New(), "Other", new(2026, 10, 1), "x", UtcNow);
        db.CareRecords.Add(other);
        db.SaveChanges();

        var result = await new UpdateFamilyCareRecordCommandHandler(db, new FixedClock()).Handle(
            new UpdateFamilyCareRecordCommand(
                family.OwnerUserId, elderly.Id, other.Id, "New", new(2026, 10, 2), "new", 1, "corr"),
            CancellationToken.None);

        Assert.Equal(CareRecordErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task FamilyDelete_RemovesRecord_KeepsDeletedAudit()
    {
        var (db, family, elderly) = SeedFamily();
        var record = CareRecord.CreateForFamily(
            elderly.Id, family.OwnerUserId, family.Id, "Doomed", new(2026, 10, 1), "x", UtcNow);
        db.CareRecords.Add(record);
        db.SaveChanges();

        var result = await new DeleteFamilyCareRecordCommandHandler(db, new FixedClock()).Handle(
            new DeleteFamilyCareRecordCommand(family.OwnerUserId, elderly.Id, record.Id, record.Version, "corr"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(await db.CareRecords.SingleOrDefaultAsync(r => r.Id == record.Id));
        var audit = await db.CareRecordAudits.SingleAsync();
        Assert.Equal(CareRecordAuditAction.Deleted, audit.Action);
        Assert.Equal(record.Id, audit.RecordId);
        Assert.Equal(CareRecordSource.Family, audit.Source); // deletion attribution survives
    }

    // ========================== Caregiver surface ==========================

    [Fact]
    public async Task CaregiverCreate_ConfirmedBookingWindow_Allowed()
    {
        var (db, family, elderly) = SeedFamily();
        var caregiverId = ConfirmedBooking(db, family, elderly, UtcNow);
        var caregiverUserId = UserId.New();

        var result = await new CreateCaregiverCareRecordCommandHandler(db, new FixedClock(), new StubIdempotentResponseSnapshotter()).Handle(
            new CreateCaregiverCareRecordCommand(
                caregiverId, caregiverUserId, elderly.Id, "Field Hospital",
                new(2026, 10, 20), "dressing changed", "corr"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(CareRecordSource.Caregiver, result.Value.Value.Source);
        Assert.Equal(elderly.Id.Value, result.Value.Value.DependentId);
    }

    [Fact]
    public async Task CaregiverCreate_InProgressBooking_Allowed()
    {
        var (db, family, elderly) = SeedFamily();
        var caregiverId = ConfirmedBooking(db, family, elderly, UtcNow);
        var booking = db.Bookings.Single();
        booking.StartVisit(UtcNow.AddHours(2)); // after ConfirmedOnUtc (= now + 1m)
        db.SaveChanges();

        var result = await new CreateCaregiverCareRecordCommandHandler(db, new FixedClock(), new StubIdempotentResponseSnapshotter()).Handle(
            new CreateCaregiverCareRecordCommand(
                caregiverId, UserId.New(), elderly.Id, "Field Hospital",
                new(2026, 10, 20), "dressing changed", "corr"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task CaregiverCreate_CompletionGrace_BoundaryIsInclusive()
    {
        var (db, family, elderly) = SeedFamily();
        DateTime cutoffUtc = UtcNow.AddDays(-30); // locked 30-day grace

        // Exactly at the 30-day cutoff (inclusive) → allowed. The booking is
        // confirmed a day earlier so the visit timeline can legitimately end
        // at the cutoff instant.
        var caregiverId = ConfirmedBooking(db, family, elderly, UtcNow.AddDays(-31));
        var booking = db.Bookings.Single();
        booking.StartVisit(UtcNow.AddDays(-31).AddHours(2));
        booking.CompleteVisit("done", cutoffUtc);
        db.SaveChanges();

        var atBoundary = await new CreateCaregiverCareRecordCommandHandler(db, new FixedClock(), new StubIdempotentResponseSnapshotter()).Handle(
            new CreateCaregiverCareRecordCommand(
                caregiverId, UserId.New(), elderly.Id, "Field Hospital",
                new(2026, 10, 20), "note", "corr"),
            CancellationToken.None);
        Assert.True(atBoundary.IsSuccess);

        // One second before the cutoff (31st day) → denied.
        var (db2, family2, elderly2) = SeedFamily();
        var caregiverId2 = ConfirmedBooking(db2, family2, elderly2, UtcNow.AddDays(-31));
        var booking2 = db2.Bookings.Single();
        booking2.StartVisit(UtcNow.AddDays(-31).AddHours(2));
        booking2.CompleteVisit("done", cutoffUtc.AddSeconds(-1));
        db2.SaveChanges();

        var pastBoundary = await new CreateCaregiverCareRecordCommandHandler(db2, new FixedClock(), new StubIdempotentResponseSnapshotter()).Handle(
            new CreateCaregiverCareRecordCommand(
                caregiverId2, UserId.New(), elderly2.Id, "Field Hospital",
                new(2026, 10, 20), "note", "corr"),
            CancellationToken.None);
        Assert.Equal(CareRecordErrors.AccessDenied, pastBoundary.Error);
    }

    [Fact]
    public async Task CaregiverCreate_ForeignDependentIs403()
    {
        var (db, _, _) = SeedFamily();

        var result = await new CreateCaregiverCareRecordCommandHandler(db, new FixedClock(), new StubIdempotentResponseSnapshotter()).Handle(
            new CreateCaregiverCareRecordCommand(
                CaregiverId.New(), UserId.New(), ElderlyId.New(), "Field Hospital",
                new(2026, 10, 20), "note", "corr"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CareRecordErrors.AccessDenied, result.Error);
    }

    [Fact]
    public async Task CaregiverCreate_PermissionRevokedBetweenPickerAndCreate_IsDenied()
    {
        var (db, family, elderly) = SeedFamily();
        var caregiverId = ConfirmedBooking(db, family, elderly, UtcNow);

        // Picker sees the dependent while the booking is Confirmed.
        var picker = await new ListCaregiverRecipientsQueryHandler(db, new FixedClock()).Handle(
            new ListCaregiverRecipientsQuery(caregiverId),
            CancellationToken.None);
        Assert.True(picker.IsSuccess);
        Assert.Single(picker.Value.Items);

        // The family cancels the booking before the caregiver's POST lands.
        db.Bookings.Single().CancelByFamily("plans changed", UtcNow.AddMinutes(5));
        db.SaveChanges();

        var create = await new CreateCaregiverCareRecordCommandHandler(db, new FixedClock(), new StubIdempotentResponseSnapshotter()).Handle(
            new CreateCaregiverCareRecordCommand(
                caregiverId, UserId.New(), elderly.Id, "Field Hospital",
                new(2026, 10, 20), "note", "corr"),
            CancellationToken.None);

        Assert.True(create.IsFailure);
        Assert.Equal(CareRecordErrors.AccessDenied, create.Error);
    }

    [Fact]
    public async Task CaregiverList_And_Detail_AreSourceScoped()
    {
        var (db, family, elderly) = SeedFamily();
        var caregiverId = ConfirmedBooking(db, family, elderly, UtcNow);
        var own = CareRecord.CreateForCaregiver(elderly.Id, UserId.New(), caregiverId, "Own", new(2026, 10, 5), "x", UtcNow);
        var foreignSource = CareRecord.CreateForFamily(elderly.Id, family.OwnerUserId, family.Id, "Family Row", new(2026, 10, 6), "x", UtcNow);
        var otherDependent = CareRecord.CreateForCaregiver(ElderlyId.New(), UserId.New(), CaregiverId.New(), "Other", new(2026, 10, 7), "x", UtcNow);
        db.CareRecords.AddRange(own, foreignSource, otherDependent);
        db.SaveChanges();

        var list = await new ListCaregiverCareRecordsQueryHandler(db, new FixedClock()).Handle(
            new ListCaregiverCareRecordsQuery(caregiverId, elderly.Id),
            CancellationToken.None);
        Assert.True(list.IsSuccess);
        Assert.Single(list.Value.Items);
        Assert.Equal(CareRecordSource.Caregiver, list.Value.Items[0].Source);

        var crossSource = await new GetCaregiverCareRecordQueryHandler(db, new FixedClock()).Handle(
            new GetCaregiverCareRecordQuery(caregiverId, foreignSource.Id),
            CancellationToken.None);
        Assert.Equal(CareRecordErrors.AccessDenied, crossSource.Error);

        var unknown = await new GetCaregiverCareRecordQueryHandler(db, new FixedClock()).Handle(
            new GetCaregiverCareRecordQuery(caregiverId, Guid.NewGuid()),
            CancellationToken.None);
        Assert.Equal(CareRecordErrors.NotFound, unknown.Error);
    }

    [Fact]
    public async Task CaregiverUpdate_AfterGraceExpiryIsDenied()
    {
        var (db, family, elderly) = SeedFamily();
        var caregiverId = ConfirmedBooking(db, family, elderly, UtcNow.AddDays(-31));
        var record = CareRecord.CreateForCaregiver(elderly.Id, UserId.New(), caregiverId, "Record", new(2026, 10, 5), "x", UtcNow);
        db.CareRecords.Add(record);
        var booking = db.Bookings.Single();
        booking.StartVisit(UtcNow.AddDays(-31).AddHours(2));
        booking.CompleteVisit("done", UtcNow.AddDays(-30).AddSeconds(-1)); // outside the grace
        db.SaveChanges();

        var result = await new UpdateCaregiverCareRecordCommandHandler(db, new FixedClock()).Handle(
            new UpdateCaregiverCareRecordCommand(
                caregiverId, UserId.New(), record.Id, "Edit", new(2026, 10, 6), "y", record.Version, "corr"),
            CancellationToken.None);

        Assert.Equal(CareRecordErrors.AccessDenied, result.Error);
    }

    [Fact]
    public async Task CaregiverDelete_StaleVersionIs409()
    {
        var (db, family, elderly) = SeedFamily();
        var caregiverId = ConfirmedBooking(db, family, elderly, UtcNow);
        var record = CareRecord.CreateForCaregiver(elderly.Id, UserId.New(), caregiverId, "Record", new(2026, 10, 5), "x", UtcNow);
        db.CareRecords.Add(record);
        db.SaveChanges();

        var result = await new DeleteCaregiverCareRecordCommandHandler(db, new FixedClock()).Handle(
            new DeleteCaregiverCareRecordCommand(caregiverId, UserId.New(), record.Id, record.Version + 1, "corr"),
            CancellationToken.None);

        Assert.Equal(CareRecordErrors.ConcurrencyConflict, result.Error);
    }

    // =========================== Elderly surface ===========================

    [Fact]
    public async Task ElderlyList_SelfOnly_AllSources_ReadOnlyByConstruction()
    {
        var (db, family, elderly) = SeedFamily();
        db.CareRecords.AddRange(
            CareRecord.CreateForFamily(elderly.Id, family.OwnerUserId, family.Id, "A", new(2026, 10, 1), "x", UtcNow),
            CareRecord.CreateForCaregiver(elderly.Id, UserId.New(), CaregiverId.New(), "B", new(2026, 10, 2), "x", UtcNow),
            CareRecord.CreateForCareHome(elderly.Id, UserId.New(), Guid.NewGuid(), "C", new(2026, 10, 3), "x", UtcNow),
            // A different dependent never leaks into another elderly's timeline.
            CareRecord.CreateForCaregiver(ElderlyId.New(), UserId.New(), CaregiverId.New(), "D", new(2026, 10, 4), "x", UtcNow));
        db.SaveChanges();

        var result = await new ListElderlyCareRecordsQueryHandler(db).Handle(
            new ListElderlyCareRecordsQuery(elderly.IdentityUserId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.TotalCount);

        // The query contract carries no dependent id at all — the dependent is
        // always derived from the authenticated elderly identity.
        Assert.DoesNotContain(
            typeof(ListElderlyCareRecordsQuery).GetConstructors().SelectMany(c => c.GetParameters()),
            p => p.Name!.Contains("Dependent", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ElderlyList_UnregisteredIdentityIsAccessDenied()
    {
        var (db, _, _) = SeedFamily();

        var result = await new ListElderlyCareRecordsQueryHandler(db).Handle(
            new ListElderlyCareRecordsQuery(UserId.New()),
            CancellationToken.None);

        Assert.Equal(CareRecordErrors.AccessDenied, result.Error);
    }

    // =========================== CareHome surface ===========================

    [Fact]
    public async Task CareHomeCreate_BindsFacilityAttributionAndAudit()
    {
        var (db, _, elderly) = SeedFamily();
        var facilityId = Guid.NewGuid();
        var ownerId = UserId.New();

        var create = await new CreateCareHomeCareRecordCommandHandler(db, new FixedClock()).Handle(
            new CreateCareHomeCareRecordCommand(
                facilityId, ownerId, elderly.Id, "Residence Clinic",
                new(2026, 10, 15), "fall observed", "corr"),
            CancellationToken.None);

        Assert.True(create.IsSuccess);
        Assert.Equal(CareRecordSource.CareHome, create.Value.Source);
        var stored = await db.CareRecords.SingleAsync(r => r.Id == create.Value.Id);
        Assert.Equal(facilityId, stored.AuthorFacilityId);

        var audit = await db.CareRecordAudits.SingleAsync();
        Assert.Equal(CareRecordAuditAction.Created, audit.Action);
        Assert.Equal(CareRecordSource.CareHome, audit.Source);
    }

    [Fact]
    public async Task CareHomeList_ScopesToFacilityAttribution()
    {
        var (db, _, elderly) = SeedFamily();
        var mine = Guid.NewGuid();
        var otherFacility = Guid.NewGuid();
        db.CareRecords.AddRange(
            CareRecord.CreateForCareHome(elderly.Id, UserId.New(), mine, "Mine", new(2026, 10, 1), "x", UtcNow),
            CareRecord.CreateForCareHome(elderly.Id, UserId.New(), otherFacility, "Other", new(2026, 10, 2), "x", UtcNow));
        db.SaveChanges();

        var result = await new ListCareHomeCareRecordsQueryHandler(db).Handle(
            new ListCareHomeCareRecordsQuery(mine, elderly.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Items);
        Assert.Equal("Mine", result.Value.Items[0].HospitalName);
    }

    [Fact]
    public async Task CareHomeList_UnknownResidentIsDependentNotFound()
    {
        var (db, _, _) = SeedFamily();

        var result = await new ListCareHomeCareRecordsQueryHandler(db).Handle(
            new ListCareHomeCareRecordsQuery(Guid.NewGuid(), ElderlyId.New()),
            CancellationToken.None);

        Assert.Equal(CareRecordErrors.DependentNotFound, result.Error);
    }

    [Fact]
    public async Task CareHomeUpdateDelete_ForeignFacilityIsAccessDenied()
    {
        var (db, _, elderly) = SeedFamily();
        var authoringFacility = Guid.NewGuid();
        var record = CareRecord.CreateForCareHome(
            elderly.Id, UserId.New(), authoringFacility, "Row", new(2026, 10, 1), "x", UtcNow);
        db.CareRecords.Add(record);
        db.SaveChanges();

        var update = await new UpdateCareHomeCareRecordCommandHandler(db, new FixedClock()).Handle(
            new UpdateCareHomeCareRecordCommand(
                Guid.NewGuid(), UserId.New(), record.Id, "Edit", new(2026, 10, 2), "y", record.Version, "corr"),
            CancellationToken.None);
        Assert.Equal(CareRecordErrors.AccessDenied, update.Error);

        var delete = await new DeleteCareHomeCareRecordCommandHandler(db, new FixedClock()).Handle(
            new DeleteCareHomeCareRecordCommand(Guid.NewGuid(), UserId.New(), record.Id, record.Version, "corr"),
            CancellationToken.None);
        Assert.Equal(CareRecordErrors.AccessDenied, delete.Error);
    }

    [Fact]
    public async Task CareHomeUpdate_StaleVersionIs409()
    {
        var (db, _, elderly) = SeedFamily();
        var facilityId = Guid.NewGuid();
        var record = CareRecord.CreateForCareHome(
            elderly.Id, UserId.New(), facilityId, "Row", new(2026, 10, 1), "x", UtcNow);
        db.CareRecords.Add(record);
        db.SaveChanges();

        var result = await new UpdateCareHomeCareRecordCommandHandler(db, new FixedClock()).Handle(
            new UpdateCareHomeCareRecordCommand(
                facilityId, UserId.New(), record.Id, "Edit", new(2026, 10, 2), "y", record.Version + 3, "corr"),
            CancellationToken.None);

        Assert.Equal(CareRecordErrors.ConcurrencyConflict, result.Error);
    }

    // ====================== Persistence-model integrity ======================

    [Fact]
    public void PersistenceModel_CareRecordElderlyForeignKey_IsRestrict()
    {
        using var db = CreateDbContext();

        var entityType = db.Model.FindEntityType(typeof(CareRecord));
        Assert.NotNull(entityType);

        // Exactly one real relationship: the dependent. Author family/caregiver
        // ids stay opaque cross-module or Family-internal values with no FK.
        var fk = Assert.Single(entityType!.GetForeignKeys());
        Assert.Equal(typeof(Elderly), fk.PrincipalEntityType.ClrType);
        Assert.Equal(nameof(CareRecord.ElderlyId), Assert.Single(fk.Properties).Name);
        Assert.True(fk.IsRequired);

        // Care records are medical data: deleting a dependent (or anything
        // else) must never cascade-delete them.
        Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior);

        // The converted strong-id column keeps its pinned relational name.
        Assert.Equal(
            "elderly_id",
            entityType.FindProperty(nameof(CareRecord.ElderlyId))!
                .GetColumnName(StoreObjectIdentifier.Table("care_records", "families")));
    }

    // ============================ Admin surface ============================

    [Fact]
    public async Task AdminList_CombinesSources_FiltersAndAuditsPayloadFree()
    {
        var (db, _, elderly) = SeedFamily();
        db.CareRecords.AddRange(
            CareRecord.CreateForFamily(elderly.Id, UserId.New(), FamilyId.New(), "A", new(2026, 10, 1), "x", UtcNow),
            CareRecord.CreateForCaregiver(elderly.Id, UserId.New(), CaregiverId.New(), "B", new(2026, 10, 2), "x", UtcNow));
        db.SaveChanges();
        var adminId = UserId.New();

        var result = await new ListAdminCareRecordsQueryHandler(db, new FixedClock()).Handle(
            new ListAdminCareRecordsQuery(
                adminId, Source: "caregiver", DependentId: elderly.Id.Value,
                CorrelationId: "care-record-admin-list-test"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Items);

        var audit = await db.CareRecordAudits.SingleAsync();
        Assert.Equal(CareRecordAuditAction.AdminListed, audit.Action);
        Assert.Equal(adminId, audit.ActorUserId);
        Assert.Null(audit.RecordId);
        Assert.Null(audit.Source); // payload-free: the read never invents a source
    }

    [Fact]
    public async Task AdminList_UnknownSourceTextIsInvalidQuery()
    {
        var (db, _, _) = SeedFamily();

        var result = await new ListAdminCareRecordsQueryHandler(db, new FixedClock()).Handle(
            new ListAdminCareRecordsQuery(UserId.New(), Source: "Unknown"),
            CancellationToken.None);

        Assert.Equal(CareRecordErrors.InvalidQuery, result.Error);
    }

    [Fact]
    public async Task AdminDetail_RecordsReadAuditWithRecordId_ButNoSource()
    {
        var (db, _, elderly) = SeedFamily();
        var record = CareRecord.CreateForCaregiver(elderly.Id, UserId.New(), CaregiverId.New(), "B", new(2026, 10, 2), "x", UtcNow);
        db.CareRecords.Add(record);
        db.SaveChanges();

        var found = await new GetAdminCareRecordQueryHandler(db, new FixedClock()).Handle(
            new GetAdminCareRecordQuery(
                UserId.New(), record.Id, "care-record-admin-detail-test"),
            CancellationToken.None);
        Assert.True(found.IsSuccess);
        Assert.Equal(record.Id, found.Value.Id);

        var missing = await new GetAdminCareRecordQueryHandler(db, new FixedClock()).Handle(
            new GetAdminCareRecordQuery(UserId.New(), Guid.NewGuid()),
            CancellationToken.None);
        Assert.Equal(CareRecordErrors.NotFound, missing.Error);

        var audit = await db.CareRecordAudits.SingleAsync(a => a.Action == CareRecordAuditAction.AdminDetailRead && a.RecordId == record.Id);
        Assert.Null(audit.Source);
    }
}
