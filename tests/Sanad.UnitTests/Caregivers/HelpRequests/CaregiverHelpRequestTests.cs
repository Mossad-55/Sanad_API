using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Caregivers.Infrastructure.Persistence;
using Sanad.Modules.Caregivers.Application.HelpRequests;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Domain.HelpRequests;
using Sanad.Modules.Families.Infrastructure.Persistence;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.UnitTests.Caregivers.HelpRequests;

public sealed class CaregiverHelpRequestTests
{
    [Fact]
    public async Task AcceptCaregiverHelpRequest_Success()
    {
        await using var db = CreateCaregiversDb();
        var familiesDatabaseName = Guid.NewGuid().ToString();
        await using var familiesDb = CreateFamiliesDb(familiesDatabaseName);

        // Setup: family, elderly, caregiver with booking
        var owner = UserId.New();
        var elderlyIdentity = UserId.New();
        var family = Family.Create(owner, "family");
        var elderly = Elderly.Create(owner, elderlyIdentity, family.Id, FamilyRelationshipType.Father,
            FullName.Create("عمر"), FullName.Create("Omar"), Gender.Male, new DateOnly(1950, 1, 1), new DateOnly(2020, 1, 1));

        var caregiver = UserId.New();
        var caregiverProfile = Caregiver.Create(caregiver, CaregiverType.Medical);

        // Booking linking caregiver to elderly
        var booking = CreateBooking(
            family.Id,
            elderly.Id,
            caregiverProfile.Id,
            BookingStatus.Confirmed,
            DateOnly.FromDateTime(DateTime.UtcNow),
            DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            "notes");

        familiesDb.Families.Add(family);
        familiesDb.Elderlies.Add(elderly);
        familiesDb.Bookings.Add(booking);
        await familiesDb.SaveChangesAsync();

        db.Caregivers.Add(caregiverProfile);
        await db.SaveChangesAsync();

        // Create a help request for the elderly
        var request = CreateHelpRequest(elderlyIdentity);
        familiesDb.ElderlyHelpRequests.Add(request);
        await familiesDb.SaveChangesAsync();

        var handler = new AcceptCaregiverHelpRequestCommandHandler(
            db,
            familiesDb);

        var command = new AcceptCaregiverHelpRequestCommand(caregiver, request.Id);
        var result = await handler.Handle(command, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(ElderlyHelpRequestStatus.Accepted, result.Value.Status);
        Assert.Equal(request.Id, result.Value.Id);
        Assert.Equal(elderly.Id.Value, result.Value.ElderlyId);
        Assert.Equal(2, familiesDb.ElderlyHelpRequestHistories.Count());
        Assert.Equal(ElderlyHelpRequestHistoryAction.Accepted, familiesDb.ElderlyHelpRequestHistories.Last().Action);
        Assert.Equal(ElderlyHelpRequestStatus.Accepted, familiesDb.ElderlyHelpRequestHistories.Last().Status);

        await using var persistedFamiliesDb = CreateFamiliesDb(familiesDatabaseName);
        var persistedHelpRequest = await persistedFamiliesDb.ElderlyHelpRequests
            .AsNoTracking()
            .SingleAsync(item => item.Id == request.Id);
        Assert.Equal(ElderlyHelpRequestStatus.Accepted, persistedHelpRequest.Status);
    }

    [Fact]
    public async Task DeclineCaregiverHelpRequest_Success()
    {
        await using var db = CreateCaregiversDb();
        var familiesDatabaseName = Guid.NewGuid().ToString();
        await using var familiesDb = CreateFamiliesDb(familiesDatabaseName);

        // Setup: family, elderly, caregiver with booking
        var owner = UserId.New();
        var elderlyIdentity = UserId.New();
        var family = Family.Create(owner, "family");
        var elderly = Elderly.Create(owner, elderlyIdentity, family.Id, FamilyRelationshipType.Father,
            FullName.Create("عمر"), FullName.Create("Omar"), Gender.Male, new DateOnly(1950, 1, 1), new DateOnly(2020, 1, 1));

        var caregiver = UserId.New();
        var caregiverProfile = Caregiver.Create(caregiver, CaregiverType.Medical);

        // Booking linking caregiver to elderly
        var booking = CreateBooking(
            family.Id,
            elderly.Id,
            caregiverProfile.Id,
            BookingStatus.Confirmed,
            DateOnly.FromDateTime(DateTime.UtcNow),
            DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            "notes");

        familiesDb.Families.Add(family);
        familiesDb.Elderlies.Add(elderly);
        familiesDb.Bookings.Add(booking);
        await familiesDb.SaveChangesAsync();

        db.Caregivers.Add(caregiverProfile);
        await db.SaveChangesAsync();

        // Create a help request for the elderly
        var request = CreateHelpRequest(elderlyIdentity);
        familiesDb.ElderlyHelpRequests.Add(request);
        await familiesDb.SaveChangesAsync();

        var handler = new DeclineCaregiverHelpRequestCommandHandler(
            db,
            familiesDb);

        var command = new DeclineCaregiverHelpRequestCommand(caregiver, request.Id, "Caregiver unavailable.");
        var result = await handler.Handle(command, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(ElderlyHelpRequestStatus.Rejected, result.Value.Status);
        Assert.Equal(request.Id, result.Value.Id);
        Assert.Equal(elderly.Id.Value, result.Value.ElderlyId);
        Assert.Equal(2, familiesDb.ElderlyHelpRequestHistories.Count());
        Assert.Equal(ElderlyHelpRequestHistoryAction.Rejected, familiesDb.ElderlyHelpRequestHistories.Last().Action);
        Assert.Equal(ElderlyHelpRequestStatus.Rejected, familiesDb.ElderlyHelpRequestHistories.Last().Status);

        await using var persistedFamiliesDb = CreateFamiliesDb(familiesDatabaseName);
        var persistedHelpRequest = await persistedFamiliesDb.ElderlyHelpRequests
            .AsNoTracking()
            .SingleAsync(item => item.Id == request.Id);
        Assert.Equal(ElderlyHelpRequestStatus.Rejected, persistedHelpRequest.Status);
    }

    [Fact]
    public async Task AcceptCaregiverHelpRequest_FailsWhenNotCaregiver()
    {
        await using var db = CreateCaregiversDb();
        await using var familiesDb = CreateFamiliesDb();

        // Setup: family, elderly, caregiver with booking
        var owner = UserId.New();
        var elderlyIdentity = UserId.New();
        var family = Family.Create(owner, "family");
        var elderly = Elderly.Create(owner, elderlyIdentity, family.Id, FamilyRelationshipType.Father,
            FullName.Create("عمر"), FullName.Create("Omar"), Gender.Male, new DateOnly(1950, 1, 1), new DateOnly(2020, 1, 1));

        var caregiver = UserId.New();
        var caregiverProfile = Caregiver.Create(caregiver, CaregiverType.Medical);

        // Booking linking caregiver to elderly
        var booking = CreateBooking(
            family.Id,
            elderly.Id,
            caregiverProfile.Id,
            BookingStatus.Confirmed,
            DateOnly.FromDateTime(DateTime.UtcNow),
            DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            "notes");

        familiesDb.Families.Add(family);
        familiesDb.Elderlies.Add(elderly);
        familiesDb.Bookings.Add(booking);
        await familiesDb.SaveChangesAsync();

        db.Caregivers.Add(caregiverProfile);
        await db.SaveChangesAsync();

        // Create a help request for the elderly
        var request = CreateHelpRequest(elderlyIdentity);
        familiesDb.ElderlyHelpRequests.Add(request);
        await familiesDb.SaveChangesAsync();

        var nonCaregiver = UserId.New();
        var handler = new AcceptCaregiverHelpRequestCommandHandler(
            db,
            familiesDb);

        var command = new AcceptCaregiverHelpRequestCommand(nonCaregiver, request.Id);
        var result = await handler.Handle(command, default);

        Assert.True(result.IsFailure);
        Assert.Equal("Caregivers.HelpRequest.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task AcceptCaregiverHelpRequest_FailsWhenElderlyNotInBooking()
    {
        await using var db = CreateCaregiversDb();
        await using var familiesDb = CreateFamiliesDb();

        // Setup: family, elderly1 (with booking), elderly2 (no booking)
        var owner = UserId.New();
        var elderlyIdentity1 = UserId.New();
        var elderlyIdentity2 = UserId.New();
        var family = Family.Create(owner, "family");
        var elderly1 = Elderly.Create(owner, elderlyIdentity1, family.Id, FamilyRelationshipType.Father,
            FullName.Create("عمر"), FullName.Create("Omar"), Gender.Male, new DateOnly(1950, 1, 1), new DateOnly(2020, 1, 1));
        var elderly2 = Elderly.Create(owner, elderlyIdentity2, family.Id, FamilyRelationshipType.Mother,
            FullName.Create("أميرة"), FullName.Create("Amira"), Gender.Female, new DateOnly(1955, 1, 1), new DateOnly(2021, 1, 1));

        var caregiver = UserId.New();
        var caregiverProfile = Caregiver.Create(caregiver, CaregiverType.Medical);

        // Booking linking caregiver to elderly1 only
        var booking = CreateBooking(
            family.Id,
            elderly1.Id,
            caregiverProfile.Id,
            BookingStatus.Confirmed,
            DateOnly.FromDateTime(DateTime.UtcNow),
            DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            "notes");

        familiesDb.Families.Add(family);
        familiesDb.Elderlies.AddRange(elderly1, elderly2);
        familiesDb.Bookings.Add(booking);
        await familiesDb.SaveChangesAsync();

        db.Caregivers.Add(caregiverProfile);
        await db.SaveChangesAsync();

        // Create a help request for elderly2 (not booked)
        var request = CreateHelpRequest(elderlyIdentity2);
        familiesDb.ElderlyHelpRequests.Add(request);
        await familiesDb.SaveChangesAsync();

        var handler = new AcceptCaregiverHelpRequestCommandHandler(
            db,
            familiesDb);

        var command = new AcceptCaregiverHelpRequestCommand(caregiver, request.Id);
        var result = await handler.Handle(command, default);

        Assert.True(result.IsFailure);
        Assert.Equal("Caregivers.HelpRequest.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task AcceptCaregiverHelpRequest_FailsWhenHelpRequestNotFound()
    {
        await using var db = CreateCaregiversDb();
        await using var familiesDb = CreateFamiliesDb();

        // Setup: family, elderly, caregiver with booking
        var owner = UserId.New();
        var elderlyIdentity = UserId.New();
        var family = Family.Create(owner, "family");
        var elderly = Elderly.Create(owner, elderlyIdentity, family.Id, FamilyRelationshipType.Father,
            FullName.Create("عمر"), FullName.Create("Omar"), Gender.Male, new DateOnly(1950, 1, 1), new DateOnly(2020, 1, 1));

        var caregiver = UserId.New();
        var caregiverProfile = Caregiver.Create(caregiver, CaregiverType.Medical);

        // Booking linking caregiver to elderly
        var booking = CreateBooking(
            family.Id,
            elderly.Id,
            caregiverProfile.Id,
            BookingStatus.Confirmed,
            DateOnly.FromDateTime(DateTime.UtcNow),
            DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            "notes");

        familiesDb.Families.Add(family);
        familiesDb.Elderlies.Add(elderly);
        familiesDb.Bookings.Add(booking);
        await familiesDb.SaveChangesAsync();

        db.Caregivers.Add(caregiverProfile);
        await db.SaveChangesAsync();

        var handler = new AcceptCaregiverHelpRequestCommandHandler(
            db,
            familiesDb);

        var command = new AcceptCaregiverHelpRequestCommand(caregiver, Guid.NewGuid());
        var result = await handler.Handle(command, default);

        Assert.True(result.IsFailure);
        Assert.Equal("Caregivers.HelpRequest.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task AcceptCaregiverHelpRequest_FailsWhenHelpRequestNotPending()
    {
        await using var db = CreateCaregiversDb();
        await using var familiesDb = CreateFamiliesDb();

        // Setup: family, elderly, caregiver with booking
        var owner = UserId.New();
        var elderlyIdentity = UserId.New();
        var family = Family.Create(owner, "family");
        var elderly = Elderly.Create(owner, elderlyIdentity, family.Id, FamilyRelationshipType.Father,
            FullName.Create("عمر"), FullName.Create("Omar"), Gender.Male, new DateOnly(1950, 1, 1), new DateOnly(2020, 1, 1));

        var caregiver = UserId.New();
        var caregiverProfile = Caregiver.Create(caregiver, CaregiverType.Medical);

        // Booking linking caregiver to elderly
        var booking = CreateBooking(
            family.Id,
            elderly.Id,
            caregiverProfile.Id,
            BookingStatus.Confirmed,
            DateOnly.FromDateTime(DateTime.UtcNow),
            DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            "notes");

        familiesDb.Families.Add(family);
        familiesDb.Elderlies.Add(elderly);
        familiesDb.Bookings.Add(booking);
        await familiesDb.SaveChangesAsync();

        db.Caregivers.Add(caregiverProfile);
        await db.SaveChangesAsync();

        // Create a help request for the elderly and accept it first
        var request = CreateHelpRequest(elderlyIdentity);
        request.Transition(ElderlyHelpRequestHistoryAction.Accepted, null, elderlyIdentity);
        familiesDb.ElderlyHelpRequests.Add(request);
        await familiesDb.SaveChangesAsync();

        var handler = new AcceptCaregiverHelpRequestCommandHandler(
            db,
            familiesDb);

        var command = new AcceptCaregiverHelpRequestCommand(caregiver, request.Id);
        var result = await handler.Handle(command, default);

        Assert.True(result.IsFailure);
        Assert.Equal("Caregivers.HelpRequest.InvalidOperation", result.Error.Code);
    }

    [Fact]
    public async Task StartCaregiverHelpRequest_Success()
    {
        await using var db = CreateCaregiversDb();
        await using var familiesDb = CreateFamiliesDb();

        // Setup: family, elderly, caregiver with booking
        var owner = UserId.New();
        var elderlyIdentity = UserId.New();
        var family = Family.Create(owner, "family");
        var elderly = Elderly.Create(owner, elderlyIdentity, family.Id, FamilyRelationshipType.Father,
            FullName.Create("عمر"), FullName.Create("Omar"), Gender.Male, new DateOnly(1950, 1, 1), new DateOnly(2020, 1, 1));

        var caregiver = UserId.New();
        var caregiverProfile = Caregiver.Create(caregiver, CaregiverType.Medical);

        // Booking linking caregiver to elderly
        var booking = CreateBooking(
            family.Id,
            elderly.Id,
            caregiverProfile.Id,
            BookingStatus.Confirmed,
            DateOnly.FromDateTime(DateTime.UtcNow),
            DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            "notes");

        familiesDb.Families.Add(family);
        familiesDb.Elderlies.Add(elderly);
        familiesDb.Bookings.Add(booking);
        await familiesDb.SaveChangesAsync();

        db.Caregivers.Add(caregiverProfile);
        await db.SaveChangesAsync();

        // Create a help request for the elderly and accept it first
        var request = CreateHelpRequest(elderlyIdentity);
        request.Transition(ElderlyHelpRequestHistoryAction.Accepted, null, elderlyIdentity);
        familiesDb.ElderlyHelpRequests.Add(request);
        await familiesDb.SaveChangesAsync();

        var handler = new StartCaregiverHelpRequestCommandHandler(
            db,
            familiesDb);

        var command = new StartCaregiverHelpRequestCommand(caregiver, request.Id);
        var result = await handler.Handle(command, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(ElderlyHelpRequestStatus.InProgress, result.Value.Status);
        Assert.Equal(request.Id, result.Value.Id);
        Assert.Equal(elderly.Id.Value, result.Value.ElderlyId);
        Assert.NotNull(familiesDb.ElderlyHelpRequestHistories.LastOrDefault(x => x.Action == ElderlyHelpRequestHistoryAction.Started));
        Assert.Equal(ElderlyHelpRequestHistoryAction.Started, familiesDb.ElderlyHelpRequestHistories.Last(x => x.Action == ElderlyHelpRequestHistoryAction.Started).Action);
        Assert.Equal(ElderlyHelpRequestStatus.InProgress, familiesDb.ElderlyHelpRequestHistories.Last(x => x.Action == ElderlyHelpRequestHistoryAction.Started).Status);
    }

    [Fact]
    public async Task StartCaregiverHelpRequest_FailsWhenNotAccepted()
    {
        await using var db = CreateCaregiversDb();
        await using var familiesDb = CreateFamiliesDb();

        // Setup: family, elderly, caregiver with booking
        var owner = UserId.New();
        var elderlyIdentity = UserId.New();
        var family = Family.Create(owner, "family");
        var elderly = Elderly.Create(owner, elderlyIdentity, family.Id, FamilyRelationshipType.Father,
            FullName.Create("عمر"), FullName.Create("Omar"), Gender.Male, new DateOnly(1950, 1, 1), new DateOnly(2020, 1, 1));

        var caregiver = UserId.New();
        var caregiverProfile = Caregiver.Create(caregiver, CaregiverType.Medical);

        // Booking linking caregiver to elderly
        var booking = CreateBooking(
            family.Id,
            elderly.Id,
            caregiverProfile.Id,
            BookingStatus.Confirmed,
            DateOnly.FromDateTime(DateTime.UtcNow),
            DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            "notes");

        familiesDb.Families.Add(family);
        familiesDb.Elderlies.Add(elderly);
        familiesDb.Bookings.Add(booking);
        await familiesDb.SaveChangesAsync();

        db.Caregivers.Add(caregiverProfile);
        await db.SaveChangesAsync();

        // Create a help request for the elderly (still pending)
        var request = CreateHelpRequest(elderlyIdentity);
        familiesDb.ElderlyHelpRequests.Add(request);
        await familiesDb.SaveChangesAsync();

        var handler = new StartCaregiverHelpRequestCommandHandler(
            db,
            familiesDb);

        var command = new StartCaregiverHelpRequestCommand(caregiver, request.Id);
        var result = await handler.Handle(command, default);

        Assert.True(result.IsFailure);
        Assert.Equal("Caregivers.HelpRequest.InvalidOperation", result.Error.Code);
    }

    [Fact]
    public async Task ResolveCaregiverHelpRequest_Success()
    {
        await using var db = CreateCaregiversDb();
        var familiesDatabaseName = Guid.NewGuid().ToString();
        await using var familiesDb = CreateFamiliesDb(familiesDatabaseName);

        // Setup: family, elderly, caregiver with booking
        var owner = UserId.New();
        var elderlyIdentity = UserId.New();
        var family = Family.Create(owner, "family");
        var elderly = Elderly.Create(owner, elderlyIdentity, family.Id, FamilyRelationshipType.Father,
            FullName.Create("عمر"), FullName.Create("Omar"), Gender.Male, new DateOnly(1950, 1, 1), new DateOnly(2020, 1, 1));

        var caregiver = UserId.New();
        var caregiverProfile = Caregiver.Create(caregiver, CaregiverType.Medical);

        // Booking linking caregiver to elderly
        var booking = CreateBooking(
            family.Id,
            elderly.Id,
            caregiverProfile.Id,
            BookingStatus.Confirmed,
            DateOnly.FromDateTime(DateTime.UtcNow),
            DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            "notes");

        familiesDb.Families.Add(family);
        familiesDb.Elderlies.Add(elderly);
        familiesDb.Bookings.Add(booking);
        await familiesDb.SaveChangesAsync();

        db.Caregivers.Add(caregiverProfile);
        await db.SaveChangesAsync();

        // Create a help request for the elderly, accept it, and start it
        var request = CreateHelpRequest(elderlyIdentity);
        request.Transition(ElderlyHelpRequestHistoryAction.Accepted, null, elderlyIdentity);
        request.Transition(ElderlyHelpRequestHistoryAction.Started, null, caregiver); // Transition to InProgress
        familiesDb.ElderlyHelpRequests.Add(request);
        await familiesDb.SaveChangesAsync();

        var handler = new ResolveCaregiverHelpRequestCommandHandler(
            db,
            familiesDb);

        var command = new ResolveCaregiverHelpRequestCommand(caregiver, request.Id, "Care completed.");
        var result = await handler.Handle(command, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(ElderlyHelpRequestStatus.Resolved, result.Value.Status);
        Assert.Equal(request.Id, result.Value.Id);
        Assert.Equal(elderly.Id.Value, result.Value.ElderlyId);
        Assert.NotNull(familiesDb.ElderlyHelpRequestHistories.LastOrDefault(x => x.Action == ElderlyHelpRequestHistoryAction.Resolved));
        Assert.Equal(ElderlyHelpRequestHistoryAction.Resolved, familiesDb.ElderlyHelpRequestHistories.Last(x => x.Action == ElderlyHelpRequestHistoryAction.Resolved).Action);
        Assert.Equal(ElderlyHelpRequestStatus.Resolved, familiesDb.ElderlyHelpRequestHistories.Last(x => x.Action == ElderlyHelpRequestHistoryAction.Resolved).Status);

        await using var persistedFamiliesDb = CreateFamiliesDb(familiesDatabaseName);
        var persistedHelpRequest = await persistedFamiliesDb.ElderlyHelpRequests
            .AsNoTracking()
            .SingleAsync(item => item.Id == request.Id);
        Assert.Equal(ElderlyHelpRequestStatus.Resolved, persistedHelpRequest.Status);
    }

    [Fact]
    public async Task ResolveCaregiverHelpRequest_FailsWhenNotInProgress()
    {
        await using var db = CreateCaregiversDb();
        await using var familiesDb = CreateFamiliesDb();

        // Setup: family, elderly, caregiver with booking
        var owner = UserId.New();
        var elderlyIdentity = UserId.New();
        var family = Family.Create(owner, "family");
        var elderly = Elderly.Create(owner, elderlyIdentity, family.Id, FamilyRelationshipType.Father,
            FullName.Create("عمر"), FullName.Create("Omar"), Gender.Male, new DateOnly(1950, 1, 1), new DateOnly(2020, 1, 1));

        var caregiver = UserId.New();
        var caregiverProfile = Caregiver.Create(caregiver, CaregiverType.Medical);

        // Booking linking caregiver to elderly
        var booking = CreateBooking(
            family.Id,
            elderly.Id,
            caregiverProfile.Id,
            BookingStatus.Confirmed,
            DateOnly.FromDateTime(DateTime.UtcNow),
            DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            "notes");

        familiesDb.Families.Add(family);
        familiesDb.Elderlies.Add(elderly);
        familiesDb.Bookings.Add(booking);
        await familiesDb.SaveChangesAsync();

        db.Caregivers.Add(caregiverProfile);
        await db.SaveChangesAsync();

        // Create a help request for the elderly and accept it (but don't start it)
        var request = CreateHelpRequest(elderlyIdentity);
        request.Transition(ElderlyHelpRequestHistoryAction.Accepted, null, elderlyIdentity);
        familiesDb.ElderlyHelpRequests.Add(request);
        await familiesDb.SaveChangesAsync();

        var handler = new ResolveCaregiverHelpRequestCommandHandler(
            db,
            familiesDb);

        var command = new ResolveCaregiverHelpRequestCommand(caregiver, request.Id, "Care completed.");
        var result = await handler.Handle(command, default);

        Assert.True(result.IsFailure);
        Assert.Equal("Caregivers.HelpRequest.InvalidOperation", result.Error.Code);
    }

    [Fact]
    public async Task CaregiverHelpRequestTransitions_RejectMissingReason()
    {
        await using var db = CreateCaregiversDb();
        await using var familiesDb = CreateFamiliesDb();
        var actor = UserId.New();

        var declineResult = await new DeclineCaregiverHelpRequestCommandHandler(db, familiesDb)
            .Handle(new DeclineCaregiverHelpRequestCommand(actor, Guid.NewGuid(), "  "), default);
        var resolveResult = await new ResolveCaregiverHelpRequestCommandHandler(db, familiesDb)
            .Handle(new ResolveCaregiverHelpRequestCommand(actor, Guid.NewGuid(), "  "), default);

        Assert.True(declineResult.IsFailure);
        Assert.Equal("Caregivers.HelpRequest.InvalidReason", declineResult.Error.Code);
        Assert.True(resolveResult.IsFailure);
        Assert.Equal("Caregivers.HelpRequest.InvalidReason", resolveResult.Error.Code);
    }

    private static ElderlyHelpRequest CreateHelpRequest(UserId actor) => ElderlyHelpRequest.Create(
        actor,
        Guid.NewGuid(),
        "elderly", "مسن", "Elderly",
        "ask", "يطلب", "asks",
        "needs", "مساعدة", "Help",
        null, null, null, null, Guid.NewGuid().ToString());

    private static CaregiversDbContext CreateCaregiversDb() => new(new DbContextOptionsBuilder<CaregiversDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static Booking CreateBooking(
        Sanad.BuildingBlocks.Domain.Primitives.Ids.FamilyId familyId,
        Sanad.BuildingBlocks.Domain.Primitives.Ids.ElderlyId elderlyId,
        Sanad.BuildingBlocks.Domain.Primitives.Ids.CaregiverId caregiverId,
        BookingStatus status,
        DateOnly bookingDate,
        DateOnly endDate,
        string notes)
    {
        var now = DateTime.UtcNow;
        var booking = Booking.Create(
            familyId,
            UserId.New(),
            elderlyId,
            caregiverId,
            BookingCaregiverType.Medical,
            BookingShiftType.HomeVisit,
            bookingDate,
            new TimeOnly(9, 0),
            new TimeOnly(10, 0),
            "Test service address",
            notes,
            BookingPriceSnapshot.Calculate(100m, 0m),
            now.AddDays(1),
            bookingDate,
            now);

        if (status is BookingStatus.Confirmed or BookingStatus.InProgress)
        {
            booking.MarkAsPaid("test-order", "test-transaction", now);
            booking.AcceptByCaregiver(now);
            if (status == BookingStatus.InProgress)
            {
                booking.StartVisit(now);
            }
        }

        return booking;
    }

    private static FamiliesDbContext CreateFamiliesDb(string? databaseName = null) => new(new DbContextOptionsBuilder<FamiliesDbContext>()
        .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString())
        .Options);

}
