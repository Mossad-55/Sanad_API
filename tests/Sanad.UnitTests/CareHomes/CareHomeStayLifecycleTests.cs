using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Application.Abstractions.Data;
using Sanad.Modules.CareHomes.Application.Bookings;
using Sanad.Modules.CareHomes.Application.Inventory;
using Sanad.Modules.CareHomes.Domain.Bookings;
using Sanad.Modules.CareHomes.Domain.Facilities;
using Sanad.Modules.CareHomes.Infrastructure.Persistence;
using Sanad.Modules.CareHomes.Infrastructure.Bookings;

namespace Sanad.UnitTests.CareHomes;

public sealed class CareHomeStayLifecycleTests
{
    private static readonly DateTime Now = new(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Shared_assignment_requires_a_bed_and_private_assignment_rejects_one()
    {
        await using var db = CreateDb();
        var owner = UserId.New();
        var facility = AddFacility(db, owner);
        var shared = AddInventory(db, facility.Id, CareHomeAllocationMode.Shared, 2);
        var sharedBooking = AddAcceptedBooking(db, facility.Id, shared.Type.Id, FamilyId.New());
        var privateInventory = AddInventory(db, facility.Id, CareHomeAllocationMode.Private, 1);
        var privateBooking = AddAcceptedBooking(db, facility.Id, privateInventory.Type.Id, FamilyId.New());
        await db.SaveChangesAsync();

        var sharedMissingBed = await new AssignCareHomeBookingHandler(db, new InlineReservationGuard()).Handle(
            new(owner, sharedBooking.Id, shared.Room.Id, null, Now), default);
        var privateWithBed = await new AssignCareHomeBookingHandler(db, new InlineReservationGuard()).Handle(
            new(owner, privateBooking.Id, privateInventory.Room.Id, privateInventory.Beds[0].Id, Now), default);

        Assert.False(sharedMissingBed.IsSuccess);
        Assert.Equal("CareHomes.Bookings.InvalidAssignment", sharedMissingBed.Error.Code);
        Assert.False(privateWithBed.IsSuccess);
        Assert.Equal("CareHomes.Bookings.InvalidAssignment", privateWithBed.Error.Code);
    }

    [Fact]
    public async Task Assignment_is_owner_scoped_and_rejects_wrong_room_bed_and_conflicting_accepted_stay()
    {
        await using var db = CreateDb();
        var owner = UserId.New();
        var otherOwner = UserId.New();
        var facility = AddFacility(db, owner);
        var otherFacility = AddFacility(db, otherOwner);
        var inventory = AddInventory(db, facility.Id, CareHomeAllocationMode.Shared, 2);
        var otherInventory = AddInventory(db, otherFacility.Id, CareHomeAllocationMode.Shared, 1);
        var first = AddAcceptedBooking(db, facility.Id, inventory.Type.Id, FamilyId.New());
        first.AssignPhysicalResource(inventory.Room.Id, inventory.Beds[0].Id, Now);
        var second = AddAcceptedBooking(db, facility.Id, inventory.Type.Id, FamilyId.New());
        var outside = AddAcceptedBooking(db, otherFacility.Id, otherInventory.Type.Id, FamilyId.New());
        await db.SaveChangesAsync();

        var wrongOwner = await new AssignCareHomeBookingHandler(db, new InlineReservationGuard()).Handle(
            new(otherOwner, second.Id, inventory.Room.Id, inventory.Beds[1].Id, Now), default);
        var wrongBed = await new AssignCareHomeBookingHandler(db, new InlineReservationGuard()).Handle(
            new(owner, second.Id, inventory.Room.Id, otherInventory.Beds[0].Id, Now), default);
        var conflict = await new AssignCareHomeBookingHandler(db, new InlineReservationGuard()).Handle(
            new(owner, second.Id, inventory.Room.Id, inventory.Beds[0].Id, Now), default);
        var missing = await new AssignCareHomeBookingHandler(db, new InlineReservationGuard()).Handle(
            new(owner, outside.Id, inventory.Room.Id, inventory.Beds[1].Id, Now), default);

        Assert.Equal("CareHomes.Bookings.NotFound", wrongOwner.Error.Code);
        Assert.Equal("CareHomes.Bookings.InvalidAssignment", wrongBed.Error.Code);
        Assert.Equal("CareHomes.Bookings.AssignmentConflict", conflict.Error.Code);
        Assert.Equal("CareHomes.Bookings.NotFound", missing.Error.Code);
    }

    [Fact]
    public async Task Accepted_shared_stay_can_be_checked_in_and_checked_out_and_opens_one_dispute()
    {
        await using var db = CreateDb();
        var owner = UserId.New();
        var facility = AddFacility(db, owner);
        var inventory = AddInventory(db, facility.Id, CareHomeAllocationMode.Shared, 2);
        var booking = AddAcceptedBooking(db, facility.Id, inventory.Type.Id, FamilyId.New());
        await db.SaveChangesAsync();
        var assignment = await new AssignCareHomeBookingHandler(db, new InlineReservationGuard()).Handle(new(owner, booking.Id, inventory.Room.Id, inventory.Beds[0].Id, Now), default);
        Assert.True(assignment.IsSuccess);

        var checkIn = await new RecordCareHomeCheckInHandler(db).Handle(new(owner, booking.Id, Now.AddHours(1)), default);
        var checkOut = await new RecordCareHomeCheckOutHandler(db).Handle(new(owner, booking.Id, Now.AddHours(9)), default);
        var duplicate = await new RecordCareHomeCheckOutHandler(db).Handle(new(owner, booking.Id, Now.AddHours(10)), default);
        var dispute = await db.CheckInDisputes.SingleAsync();

        Assert.True(checkIn.IsSuccess);
        Assert.True(checkOut.IsSuccess);
        Assert.Equal(Now.AddHours(1), booking.ActualCheckInOnUtc);
        Assert.Equal(Now.AddHours(9), booking.ActualCheckOutOnUtc);
        Assert.Equal("CareHomes.Bookings.InvalidState", duplicate.Error.Code);
        Assert.Equal(CareHomeCheckInDisputeStatus.Open, dispute.Status);
        Assert.Equal(owner, dispute.OpenedBy);
        Assert.Equal(Now.AddHours(9), dispute.CheckoutOnUtc);

        var secondRead = await new RecordCareHomeCheckOutHandler(db).Handle(new(owner, booking.Id, Now.AddHours(11)), default);
        Assert.Equal("CareHomes.Bookings.InvalidState", secondRead.Error.Code);
        Assert.Single(db.CheckInDisputes);
    }

    [Fact]
    public async Task Family_confirmation_is_family_scoped_idempotent_and_prevents_missing_confirmation_case()
    {
        await using var db = CreateDb();
        var owner = UserId.New();
        var familyUser = UserId.New();
        var family = FamilyId.New();
        var facility = AddFacility(db, owner);
        var inventory = AddInventory(db, facility.Id, CareHomeAllocationMode.Private, 1);
        var booking = AddAcceptedBooking(db, facility.Id, inventory.Type.Id, family);
        await db.SaveChangesAsync();
        Assert.True((await new AssignCareHomeBookingHandler(db, new InlineReservationGuard()).Handle(new(owner, booking.Id, inventory.Room.Id, null, Now), default)).IsSuccess);
        Assert.True((await new RecordCareHomeCheckInHandler(db).Handle(new(owner, booking.Id, Now.AddHours(1)), default)).IsSuccess);

        var wrongFamily = await new ConfirmCareHomeCheckInHandler(db).Handle(new(familyUser, FamilyId.New(), booking.Id, Now.AddHours(2)), default);
        var confirmed = await new ConfirmCareHomeCheckInHandler(db).Handle(new(familyUser, family, booking.Id, Now.AddHours(2)), default);
        var repeated = await new ConfirmCareHomeCheckInHandler(db).Handle(new(familyUser, family, booking.Id, Now.AddHours(3)), default);
        var checkout = await new RecordCareHomeCheckOutHandler(db).Handle(new(owner, booking.Id, Now.AddHours(9)), default);

        Assert.Equal("CareHomes.Bookings.NotFound", wrongFamily.Error.Code);
        Assert.True(confirmed.IsSuccess);
        Assert.True(repeated.IsSuccess);
        Assert.Equal(Now.AddHours(2), booking.FamilyCheckInConfirmedOnUtc);
        Assert.Equal(Now.AddHours(2), booking.FamilyCheckInConfirmedOnUtc);
        Assert.True(checkout.IsSuccess);
        Assert.Empty(db.CheckInDisputes);
    }

    [Fact]
    public async Task Check_in_requires_assignment_and_accepted_state_and_family_confirmation_requires_active_check_in()
    {
        await using var db = CreateDb();
        var owner = UserId.New();
        var facility = AddFacility(db, owner);
        var inventory = AddInventory(db, facility.Id, CareHomeAllocationMode.Shared, 1);
        var pending = CareHomeBooking.Create(facility.Id, UserId.New(), FamilyId.New(), ElderlyId.New(), inventory.Type.Id, new DateOnly(2026, 10, 5), 100, 0, 0, 1, "عربي", "English", 75, null, "Contact", null, null, null, Now);
        db.Bookings.Add(pending);
        await db.SaveChangesAsync();

        var unassigned = await new RecordCareHomeCheckInHandler(db).Handle(new(owner, pending.Id, Now.AddHours(1)), default);
        var familyBeforeCheckIn = await new ConfirmCareHomeCheckInHandler(db).Handle(new(UserId.New(), pending.FamilyId, pending.Id, Now.AddHours(1)), default);

        Assert.Equal("CareHomes.Bookings.InvalidState", unassigned.Error.Code);
        Assert.Equal("CareHomes.Bookings.InvalidState", familyBeforeCheckIn.Error.Code);
    }

    [Fact]
    public async Task Admin_can_list_and_resolve_dispute_with_required_audit_evidence_once()
    {
        await using var db = CreateDb();
        var owner = UserId.New();
        var facility = AddFacility(db, owner);
        var inventory = AddInventory(db, facility.Id, CareHomeAllocationMode.Private, 1);
        var booking = AddAcceptedBooking(db, facility.Id, inventory.Type.Id, FamilyId.New());
        await db.SaveChangesAsync();
        Assert.True((await new AssignCareHomeBookingHandler(db, new InlineReservationGuard()).Handle(new(owner, booking.Id, inventory.Room.Id, null, Now), default)).IsSuccess);
        Assert.True((await new RecordCareHomeCheckInHandler(db).Handle(new(owner, booking.Id, Now.AddHours(1)), default)).IsSuccess);
        Assert.True((await new RecordCareHomeCheckOutHandler(db).Handle(new(owner, booking.Id, Now.AddHours(9)), default)).IsSuccess);
        var dispute = await db.CheckInDisputes.SingleAsync();
        var handlers = new CheckInDisputeHandlers(db);

        var listed = await handlers.Handle(new ListCareHomeCheckInDisputesQuery(), default);
        var invalid = await handlers.Handle(new ResolveCareHomeCheckInDisputeCommand(UserId.New(), dispute.Id, Now.AddHours(1), " ", "reason", Now.AddHours(10)), default);
        var resolved = await handlers.Handle(new ResolveCareHomeCheckInDisputeCommand(UserId.New(), dispute.Id, Now.AddHours(1), "signed log", "Family confirmation missing at checkout", Now.AddHours(10)), default);
        var repeated = await handlers.Handle(new ResolveCareHomeCheckInDisputeCommand(UserId.New(), dispute.Id, Now.AddHours(1), "second", "second", Now.AddHours(11)), default);

        Assert.True(listed.IsSuccess);
        Assert.Single(listed.Value);
        Assert.Equal("CareHomes.CheckInDispute.InvalidState", invalid.Error.Code);
        Assert.True(resolved.IsSuccess);
        Assert.Equal(CareHomeCheckInDisputeStatus.Resolved, dispute.Status);
        Assert.Equal("signed log", dispute.Evidence);
        Assert.Equal("Family confirmation missing at checkout", dispute.Reason);
        Assert.Equal(Now.AddHours(1), dispute.EffectiveCheckInOnUtc);
        Assert.Equal("CareHomes.CheckInDispute.InvalidState", repeated.Error.Code);
    }

    [Fact]
    public async Task Family_can_open_one_dispute_with_trimmed_reason_without_changing_stay_timestamps()
    {
        await using var db = CreateDb();
        var owner = UserId.New();
        var family = FamilyId.New();
        var familyUser = UserId.New();
        var facility = AddFacility(db, owner);
        var inventory = AddInventory(db, facility.Id, CareHomeAllocationMode.Private, 1);
        var booking = AddAcceptedBooking(db, facility.Id, inventory.Type.Id, family);
        await db.SaveChangesAsync();

        Assert.True((await new AssignCareHomeBookingHandler(db, new InlineReservationGuard()).Handle(new(owner, booking.Id, inventory.Room.Id, null, Now), default)).IsSuccess);
        Assert.True((await new RecordCareHomeCheckInHandler(db).Handle(new(owner, booking.Id, Now.AddHours(1)), default)).IsSuccess);
        var checkInBefore = booking.ActualCheckInOnUtc;
        var checkOutBefore = booking.ActualCheckOutOnUtc;

        var result = await new CheckInDisputeHandlers(db).Handle(
            new SubmitCareHomeCheckInDisputeCommand(familyUser, family, booking.Id, "  Facility record is inaccurate.  ", Now.AddHours(2)), default);

        Assert.True(result.IsSuccess);
        Assert.Equal("Facility record is inaccurate.", result.Value.FamilyReason);
        Assert.Equal(CareHomeCheckInDisputeStatus.Open, result.Value.Status);
        Assert.Equal(checkInBefore, booking.ActualCheckInOnUtc);
        Assert.Equal(checkOutBefore, booking.ActualCheckOutOnUtc);
        Assert.Single(db.CheckInDisputes);
    }

    [Fact]
    public async Task Family_dispute_rejects_invalid_reason_and_booking_without_check_in()
    {
        await using var db = CreateDb();
        var owner = UserId.New();
        var family = FamilyId.New();
        var facility = AddFacility(db, owner);
        var inventory = AddInventory(db, facility.Id, CareHomeAllocationMode.Private, 1);
        var booking = AddAcceptedBooking(db, facility.Id, inventory.Type.Id, family);
        await db.SaveChangesAsync();
        var handler = new CheckInDisputeHandlers(db);

        var empty = await handler.Handle(new SubmitCareHomeCheckInDisputeCommand(UserId.New(), family, booking.Id, "  ", Now), default);
        var overlimit = await handler.Handle(new SubmitCareHomeCheckInDisputeCommand(UserId.New(), family, booking.Id, new string('x', 2001), Now), default);
        var noCheckIn = await handler.Handle(new SubmitCareHomeCheckInDisputeCommand(UserId.New(), family, booking.Id, "valid reason", Now), default);

        Assert.Equal("CareHomes.CheckInDispute.InvalidReason", empty.Error.Code);
        Assert.Equal("CareHomes.CheckInDispute.InvalidReason", overlimit.Error.Code);
        Assert.Equal("CareHomes.CheckInDispute.InvalidState", noCheckIn.Error.Code);
        Assert.Empty(db.CheckInDisputes);
    }

    [Fact]
    public async Task Family_dispute_preserves_privacy_and_repeated_submission_returns_existing_case()
    {
        await using var db = CreateDb();
        var owner = UserId.New();
        var family = FamilyId.New();
        var facility = AddFacility(db, owner);
        var inventory = AddInventory(db, facility.Id, CareHomeAllocationMode.Private, 1);
        var booking = AddAcceptedBooking(db, facility.Id, inventory.Type.Id, family);
        await db.SaveChangesAsync();
        Assert.True((await new AssignCareHomeBookingHandler(db, new InlineReservationGuard()).Handle(new(owner, booking.Id, inventory.Room.Id, null, Now), default)).IsSuccess);
        Assert.True((await new RecordCareHomeCheckInHandler(db).Handle(new(owner, booking.Id, Now.AddHours(1)), default)).IsSuccess);
        var handler = new CheckInDisputeHandlers(db);

        var wrongFamily = await handler.Handle(new SubmitCareHomeCheckInDisputeCommand(UserId.New(), FamilyId.New(), booking.Id, "wrong family", Now.AddHours(2)), default);
        var first = await handler.Handle(new SubmitCareHomeCheckInDisputeCommand(UserId.New(), family, booking.Id, "first reason", Now.AddHours(2)), default);
        var repeated = await handler.Handle(new SubmitCareHomeCheckInDisputeCommand(UserId.New(), family, booking.Id, "second reason", Now.AddHours(3)), default);

        Assert.Equal("CareHomes.CheckInDispute.NotFound", wrongFamily.Error.Code);
        Assert.True(first.IsSuccess);
        Assert.True(repeated.IsSuccess);
        Assert.Equal(first.Value.Id, repeated.Value.Id);
        Assert.Equal("first reason", repeated.Value.FamilyReason);
        Assert.Single(db.CheckInDisputes);
    }

    [Fact]
    public async Task Family_submission_colliding_with_owner_case_persists_and_returns_family_reason()
    {
        await using var db = CreateDb();
        var owner = UserId.New();
        var family = FamilyId.New();
        var facility = AddFacility(db, owner);
        var inventory = AddInventory(db, facility.Id, CareHomeAllocationMode.Private, 1);
        var booking = AddAcceptedBooking(db, facility.Id, inventory.Type.Id, family);
        await db.SaveChangesAsync();
        Assert.True((await new AssignCareHomeBookingHandler(db, new InlineReservationGuard()).Handle(new(owner, booking.Id, inventory.Room.Id, null, Now), default)).IsSuccess);
        Assert.True((await new RecordCareHomeCheckInHandler(db).Handle(new(owner, booking.Id, Now.AddHours(1)), default)).IsSuccess);
        Assert.True((await new RecordCareHomeCheckOutHandler(db).Handle(new(owner, booking.Id, Now.AddHours(9)), default)).IsSuccess);

        var ownerCase = await db.CheckInDisputes.SingleAsync();
        Assert.Null(ownerCase.FamilyReason);
        var result = await new CheckInDisputeHandlers(db).Handle(
            new SubmitCareHomeCheckInDisputeCommand(UserId.New(), family, booking.Id, "Family observed a different arrival time.", Now.AddHours(10)), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(ownerCase.Id, result.Value.Id);
        Assert.Equal("Family observed a different arrival time.", result.Value.FamilyReason);
        Assert.Equal("Family observed a different arrival time.", (await db.CheckInDisputes.SingleAsync()).FamilyReason);
    }

    [Fact]
    public async Task Family_submission_insert_race_reloads_owner_winner_attaches_reason_and_returns_winner_id()
    {
        var databaseName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<CareHomesDbContext>().UseInMemoryDatabase(databaseName).Options;
        await using var db = new CareHomesDbContext(options);
        var owner = UserId.New();
        var family = FamilyId.New();
        var facility = AddFacility(db, owner);
        var inventory = AddInventory(db, facility.Id, CareHomeAllocationMode.Private, 1);
        var booking = AddAcceptedBooking(db, facility.Id, inventory.Type.Id, family);
        await db.SaveChangesAsync();
        Assert.True((await new AssignCareHomeBookingHandler(db, new InlineReservationGuard()).Handle(new(owner, booking.Id, inventory.Room.Id, null, Now), default)).IsSuccess);
        Assert.True((await new RecordCareHomeCheckInHandler(db).Handle(new(owner, booking.Id, Now.AddHours(1)), default)).IsSuccess);

        var winnerId = Guid.Empty;
        async Task PersistOwnerWinnerAsync()
        {
            await using var racer = new CareHomesDbContext(options);
            var winner = CareHomeCheckInDispute.Open(booking.Id, facility.Id, null, owner, Now.AddHours(2));
            winnerId = winner.Id;
            racer.CheckInDisputes.Add(winner);
            await racer.SaveChangesAsync();
        }

        var result = await new CheckInDisputeHandlers(new FamilyInsertRaceCareHomesDbContext(db, PersistOwnerWinnerAsync)).Handle(
            new SubmitCareHomeCheckInDisputeCommand(UserId.New(), family, booking.Id, "  Family observed the facility time was incorrect.  ", Now.AddHours(2)), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(winnerId, result.Value.Id);
        Assert.Equal("Family observed the facility time was incorrect.", result.Value.FamilyReason);
        await using var readback = new CareHomesDbContext(options);
        var persisted = await readback.CheckInDisputes.SingleAsync(x => x.Id == winnerId);
        Assert.Equal("Family observed the facility time was incorrect.", persisted.FamilyReason);
    }

    [Fact]
    public async Task Owner_checkout_unique_open_case_race_maps_to_open_conflict()
    {
        await using var db = CreateDb();
        var owner = UserId.New();
        var facility = AddFacility(db, owner);
        var inventory = AddInventory(db, facility.Id, CareHomeAllocationMode.Private, 1);
        var booking = AddAcceptedBooking(db, facility.Id, inventory.Type.Id, FamilyId.New());
        await db.SaveChangesAsync();
        Assert.True((await new AssignCareHomeBookingHandler(db, new InlineReservationGuard()).Handle(new(owner, booking.Id, inventory.Room.Id, null, Now), default)).IsSuccess);
        Assert.True((await new RecordCareHomeCheckInHandler(db).Handle(new(owner, booking.Id, Now.AddHours(1)), default)).IsSuccess);

        var result = await new RecordCareHomeCheckOutHandler(new FailOnceCareHomesDbContext(db)).Handle(
            new RecordCareHomeCheckOutCommand(owner, booking.Id, Now.AddHours(9)), default);

        Assert.False(result.IsSuccess);
        Assert.Equal("CareHomes.CheckInDispute.OpenConflict", result.Error.Code);
    }

    [Fact]
    public async Task Family_submission_attaches_reason_to_existing_automatic_case_and_reads_persisted_reason()
    {
        await using var db = CreateDb();
        var owner = UserId.New();
        var family = FamilyId.New();
        var facility = AddFacility(db, owner);
        var inventory = AddInventory(db, facility.Id, CareHomeAllocationMode.Private, 1);
        var booking = AddAcceptedBooking(db, facility.Id, inventory.Type.Id, family);
        await db.SaveChangesAsync();
        Assert.True((await new AssignCareHomeBookingHandler(db, new InlineReservationGuard()).Handle(new(owner, booking.Id, inventory.Room.Id, null, Now), default)).IsSuccess);
        Assert.True((await new RecordCareHomeCheckInHandler(db).Handle(new(owner, booking.Id, Now.AddHours(1)), default)).IsSuccess);
        Assert.True((await new RecordCareHomeCheckOutHandler(db).Handle(new(owner, booking.Id, Now.AddHours(9)), default)).IsSuccess);

        var result = await new CheckInDisputeHandlers(db).Handle(new SubmitCareHomeCheckInDisputeCommand(UserId.New(), family, booking.Id, "The recorded check-in is disputed.", Now.AddHours(10)), default);

        Assert.True(result.IsSuccess);
        Assert.Equal("The recorded check-in is disputed.", result.Value.FamilyReason);
        Assert.Equal("The recorded check-in is disputed.", (await db.CheckInDisputes.SingleAsync()).FamilyReason);
    }

    [Fact]
    public async Task Repeated_checkout_does_not_create_a_second_open_case_after_existing_case_creation()
    {
        await using var db = CreateDb();
        var owner = UserId.New();
        var facility = AddFacility(db, owner);
        var inventory = AddInventory(db, facility.Id, CareHomeAllocationMode.Private, 1);
        var booking = AddAcceptedBooking(db, facility.Id, inventory.Type.Id, FamilyId.New());
        await db.SaveChangesAsync();
        Assert.True((await new AssignCareHomeBookingHandler(db, new InlineReservationGuard()).Handle(new(owner, booking.Id, inventory.Room.Id, null, Now), default)).IsSuccess);
        Assert.True((await new RecordCareHomeCheckInHandler(db).Handle(new(owner, booking.Id, Now.AddHours(1)), default)).IsSuccess);

        var first = await new RecordCareHomeCheckOutHandler(db).Handle(new(owner, booking.Id, Now.AddHours(9)), default);
        var second = await new RecordCareHomeCheckOutHandler(db).Handle(new(owner, booking.Id, Now.AddHours(10)), default);

        Assert.True(first.IsSuccess);
        Assert.Equal("CareHomes.Bookings.InvalidState", second.Error.Code);
        Assert.Single(db.CheckInDisputes);
    }

    [Fact]
    public async Task Shared_bookings_on_different_beds_succeed_but_same_bed_conflicts()
    {
        await using var db = CreateDb();
        var owner = UserId.New();
        var facility = AddFacility(db, owner);
        var inventory = AddInventory(db, facility.Id, CareHomeAllocationMode.Shared, 2);
        var first = AddAcceptedBooking(db, facility.Id, inventory.Type.Id, FamilyId.New());
        var second = AddAcceptedBooking(db, facility.Id, inventory.Type.Id, FamilyId.New());
        var third = AddAcceptedBooking(db, facility.Id, inventory.Type.Id, FamilyId.New());
        await db.SaveChangesAsync();
        var handler = new AssignCareHomeBookingHandler(db, new InlineReservationGuard());

        var firstResult = await handler.Handle(new(owner, first.Id, inventory.Room.Id, inventory.Beds[0].Id, Now), default);
        var secondResult = await handler.Handle(new(owner, second.Id, inventory.Room.Id, inventory.Beds[1].Id, Now), default);
        var sameBed = await handler.Handle(new(owner, third.Id, inventory.Room.Id, inventory.Beds[0].Id, Now), default);

        Assert.True(firstResult.IsSuccess);
        Assert.True(secondResult.IsSuccess);
        Assert.Equal("CareHomes.Bookings.AssignmentConflict", sameBed.Error.Code);
    }

    [Fact]
    public async Task Assignment_is_not_reassignable_and_is_denied_after_check_in()
    {
        await using var db = CreateDb();
        var owner = UserId.New();
        var facility = AddFacility(db, owner);
        var inventory = AddInventory(db, facility.Id, CareHomeAllocationMode.Shared, 2);
        var booking = AddAcceptedBooking(db, facility.Id, inventory.Type.Id, FamilyId.New());
        await db.SaveChangesAsync();
        var handler = new AssignCareHomeBookingHandler(db, new InlineReservationGuard());
        Assert.True((await handler.Handle(new(owner, booking.Id, inventory.Room.Id, inventory.Beds[0].Id, Now), default)).IsSuccess);

        var reassignment = await handler.Handle(new(owner, booking.Id, inventory.Room.Id, inventory.Beds[1].Id, Now.AddMinutes(1)), default);
        Assert.Equal("CareHomes.Bookings.InvalidState", reassignment.Error.Code);
        Assert.True((await new RecordCareHomeCheckInHandler(db).Handle(new(owner, booking.Id, Now.AddHours(1)), default)).IsSuccess);
        var afterCheckIn = await handler.Handle(new(owner, booking.Id, inventory.Room.Id, inventory.Beds[1].Id, Now.AddHours(2)), default);
        Assert.Equal("CareHomes.Bookings.InvalidState", afterCheckIn.Error.Code);
    }

    [Fact]
    public async Task Reservation_guard_serializes_assignment_and_maps_capacity_conflict()
    {
        await using var db = CreateDb();
        var owner = UserId.New();
        var facility = AddFacility(db, owner);
        var inventory = AddInventory(db, facility.Id, CareHomeAllocationMode.Shared, 1);
        var booking = AddAcceptedBooking(db, facility.Id, inventory.Type.Id, FamilyId.New());
        await db.SaveChangesAsync();

        var result = await new AssignCareHomeBookingHandler(db, new ConflictReservationGuard()).Handle(
            new(owner, booking.Id, inventory.Room.Id, inventory.Beds[0].Id, Now), default);

        Assert.False(result.IsSuccess);
        Assert.Equal("CareHomes.Bookings.AssignmentConflict", result.Error.Code);
        Assert.Null(booking.AssignedBedId);
    }

    [Fact]
    public async Task Shared_assignment_leaves_a_different_bed_available_in_occupancy_readback()
    {
        await using var db = CreateDb();
        var owner = UserId.New();
        var facility = AddFacility(db, owner);
        var inventory = AddInventory(db, facility.Id, CareHomeAllocationMode.Shared, 2);
        var booking = AddAcceptedBooking(db, facility.Id, inventory.Type.Id, FamilyId.New());
        await db.SaveChangesAsync();
        Assert.True((await new AssignCareHomeBookingHandler(db, new InlineReservationGuard()).Handle(new(owner, booking.Id, inventory.Room.Id, inventory.Beds[0].Id, Now), default)).IsSuccess);

        var active = await new CareHomeBookingOccupancyProvider(db).GetActiveAsync(facility.Id, default);
        var availability = CareHomeAvailabilityCalculator.Calculate(
            new[] { inventory.Type }, new[] { inventory.Room }, inventory.Beds, [], active, active,
            new DateOnly(2026, 10, 5), new DateOnly(2026, 11, 5)).Single();

        Assert.Equal(1, availability.AvailableBeds);
        Assert.Equal(2, availability.TotalBeds);
        Assert.DoesNotContain(active, x => x.ResourceKind == CareHomeResourceKind.Room);
        Assert.Contains(active, x => x.ResourceKind == CareHomeResourceKind.Bed && x.ResourceId == inventory.Beds[0].Id);
    }

    [Fact]
    public async Task Private_assignment_reserves_the_whole_room_and_conflicts_with_another_private_stay()
    {
        await using var db = CreateDb();
        var owner = UserId.New();
        var facility = AddFacility(db, owner);
        var inventory = AddInventory(db, facility.Id, CareHomeAllocationMode.Private, 2);
        var first = AddAcceptedBooking(db, facility.Id, inventory.Type.Id, FamilyId.New());
        var second = AddAcceptedBooking(db, facility.Id, inventory.Type.Id, FamilyId.New());
        await db.SaveChangesAsync();
        var handler = new AssignCareHomeBookingHandler(db, new InlineReservationGuard());

        var assigned = await handler.Handle(new(owner, first.Id, inventory.Room.Id, null, Now), default);
        var conflict = await handler.Handle(new(owner, second.Id, inventory.Room.Id, null, Now), default);

        Assert.True(assigned.IsSuccess);
        Assert.Equal("CareHomes.Bookings.AssignmentConflict", conflict.Error.Code);
        Assert.Null(second.AssignedRoomId);
        Assert.Null(second.AssignedBedId);
    }

    [Fact]
    public async Task Owner_operational_read_returns_assignment_and_actor_audit_but_hides_other_facility()
    {
        await using var db = CreateDb();
        var owner = UserId.New();
        var otherOwner = UserId.New();
        var facility = AddFacility(db, owner);
        var otherFacility = AddFacility(db, otherOwner);
        var inventory = AddInventory(db, facility.Id, CareHomeAllocationMode.Shared, 1);
        var otherInventory = AddInventory(db, otherFacility.Id, CareHomeAllocationMode.Shared, 1);
        var booking = AddAcceptedBooking(db, facility.Id, inventory.Type.Id, FamilyId.New());
        var otherBooking = AddAcceptedBooking(db, otherFacility.Id, otherInventory.Type.Id, FamilyId.New());
        await db.SaveChangesAsync();
        Assert.True((await new AssignCareHomeBookingHandler(db, new InlineReservationGuard()).Handle(new(owner, booking.Id, inventory.Room.Id, inventory.Beds[0].Id, Now), default)).IsSuccess);
        Assert.True((await new RecordCareHomeCheckInHandler(db).Handle(new(owner, booking.Id, Now.AddHours(1)), default)).IsSuccess);

        var handler = new OwnerCareHomeBookingOperationalHandler(db);
        var visible = await handler.Handle(new GetOwnerCareHomeBookingOperationalQuery(owner, booking.Id), default);
        var hidden = await handler.Handle(new GetOwnerCareHomeBookingOperationalQuery(owner, otherBooking.Id), default);

        Assert.True(visible.IsSuccess);
        Assert.Equal(inventory.Room.Id, visible.Value.AssignedRoomId);
        Assert.Equal(inventory.Beds[0].Id, visible.Value.AssignedBedId);
        Assert.Equal(owner.Value, visible.Value.ActualCheckInRecordedBy);
        Assert.Equal("CareHomes.Bookings.NotFound", hidden.Error.Code);
    }

    private static CareHomesDbContext CreateDb() => new(new DbContextOptionsBuilder<CareHomesDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static CareHomeFacility AddFacility(CareHomesDbContext db, UserId owner)
    {
        var facility = CareHomeFacility.CreateDraft(owner, Now);
        db.Facilities.Add(facility);
        return facility;
    }

    private static Inventory AddInventory(CareHomesDbContext db, CareHomeId facilityId, CareHomeAllocationMode mode, int bedCount)
    {
        var type = CareHomeRoomType.Create(facilityId, "غرفة", mode.ToString(), 1000, mode, Now);
        var room = CareHomeRoom.Create(facilityId, type.Id, "101", Now);
        var beds = Enumerable.Range(0, bedCount).Select(i => CareHomeBed.Create(facilityId, room.Id, $"B{i + 1}", Now)).ToArray();
        db.RoomTypes.Add(type);
        db.Rooms.Add(room);
        db.Beds.AddRange(beds);
        return new(type, room, beds);
    }

    private static CareHomeBooking AddAcceptedBooking(CareHomesDbContext db, CareHomeId facilityId, Guid roomTypeId, FamilyId familyId)
    {
        var booking = CareHomeBooking.Create(facilityId, UserId.New(), familyId, ElderlyId.New(), roomTypeId, new DateOnly(2026, 10, 5), 100, 0, 0, 1, "عربي", "English", 75, null, "Contact", null, null, null, Now);
        booking.MarkPaid(1001, Now.AddMinutes(1));
        booking.Accept(Now.AddMinutes(2));
        db.Bookings.Add(booking);
        return booking;
    }

    private sealed record Inventory(CareHomeRoomType Type, CareHomeRoom Room, CareHomeBed[] Beds);

    private sealed class FailOnceCareHomesDbContext(CareHomesDbContext inner) : ICareHomesDbContext
    {
        private bool _failed;

        public DbSet<CareHomeFacility> Facilities => inner.Facilities;
        public DbSet<CareHomeRoomType> RoomTypes => inner.RoomTypes;
        public DbSet<CareHomeRoom> Rooms => inner.Rooms;
        public DbSet<CareHomeBed> Beds => inner.Beds;
        public DbSet<CareHomeMaintenanceBlock> MaintenanceBlocks => inner.MaintenanceBlocks;
        public DbSet<CareHomeBooking> Bookings => inner.Bookings;
        public DbSet<CareHomeCheckInDispute> CheckInDisputes => inner.CheckInDisputes;

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (!_failed)
            {
                _failed = true;
                throw new DbUpdateException("Simulated concurrent open-case insert.");
            }

            return inner.SaveChangesAsync(cancellationToken);
        }
    }

    private sealed class FamilyInsertRaceCareHomesDbContext(CareHomesDbContext inner, Func<Task> persistWinner) : ICareHomesDbContext
    {
        private bool _failed;

        public DbSet<CareHomeFacility> Facilities => inner.Facilities;
        public DbSet<CareHomeRoomType> RoomTypes => inner.RoomTypes;
        public DbSet<CareHomeRoom> Rooms => inner.Rooms;
        public DbSet<CareHomeBed> Beds => inner.Beds;
        public DbSet<CareHomeMaintenanceBlock> MaintenanceBlocks => inner.MaintenanceBlocks;
        public DbSet<CareHomeBooking> Bookings => inner.Bookings;
        public DbSet<CareHomeCheckInDispute> CheckInDisputes => inner.CheckInDisputes;

        public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (!_failed)
            {
                _failed = true;
                await persistWinner();
                throw new DbUpdateException("Simulated Family insert race.");
            }

            return await inner.SaveChangesAsync(cancellationToken);
        }
    }

    private sealed class InlineReservationGuard : ICareHomeBookingReservationGuard
    {
        public Task<T> ExecuteAsync<T>(CareHomeId facilityId, Guid roomTypeId, DateOnly startDate, Func<Task<T>> action, CancellationToken cancellationToken) => action();
    }

    private sealed class ConflictReservationGuard : ICareHomeBookingReservationGuard
    {
        public Task<T> ExecuteAsync<T>(CareHomeId facilityId, Guid roomTypeId, DateOnly startDate, Func<Task<T>> action, CancellationToken cancellationToken) => throw new CareHomeCapacityConflictException();
    }
}
