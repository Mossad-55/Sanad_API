using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Application.Abstractions.Data;
using Sanad.Modules.CareHomes.Application.Bookings;
using Sanad.Modules.CareHomes.Application.Inventory;
using Sanad.Modules.CareHomes.Domain.Bookings;
using Sanad.Modules.CareHomes.Domain.Facilities;
using Sanad.Modules.CareHomes.Infrastructure.Persistence;
using Sanad.Modules.CareHomes.Infrastructure.Bookings;
using Sanad.Modules.Families.Application.Abstractions.Payments;
using Sanad.BuildingBlocks.Application.Results;

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
        var suiteInventory = AddInventory(db, facility.Id, CareHomeAllocationMode.Suite, 1);
        var suiteBooking = AddAcceptedBooking(db, facility.Id, suiteInventory.Type.Id, FamilyId.New());
        await db.SaveChangesAsync();

        var sharedMissingBed = await new AssignCareHomeBookingHandler(db, new InlineReservationGuard()).Handle(
            new(owner, sharedBooking.Id, shared.Room.Id, null, Now), default);
        var privateWithBed = await new AssignCareHomeBookingHandler(db, new InlineReservationGuard()).Handle(
            new(owner, privateBooking.Id, privateInventory.Room.Id, privateInventory.Beds[0].Id, Now), default);
        var suiteWithBed = await new AssignCareHomeBookingHandler(db, new InlineReservationGuard()).Handle(
            new(owner, suiteBooking.Id, suiteInventory.Room.Id, suiteInventory.Beds[0].Id, Now), default);

        Assert.False(sharedMissingBed.IsSuccess);
        Assert.Equal("CareHomes.Bookings.InvalidAssignment", sharedMissingBed.Error.Code);
        Assert.False(privateWithBed.IsSuccess);
        Assert.Equal("CareHomes.Bookings.InvalidAssignment", privateWithBed.Error.Code);
        Assert.False(suiteWithBed.IsSuccess);
        Assert.Equal("CareHomes.Bookings.InvalidAssignment", suiteWithBed.Error.Code);
    }

    [Fact]
    public async Task Early_check_out_fully_refunds_a_paid_extension_that_has_not_started()
    {
        await using var db = CreateDb();
        UserId owner = UserId.New();
        var facility = AddFacility(db, owner);
        Inventory inventory = AddInventory(db, facility.Id, CareHomeAllocationMode.Shared, 2);
        var original = AddAcceptedBooking(db, facility.Id, inventory.Type.Id, FamilyId.New());
        original.AssignPhysicalResource(inventory.Room.Id, inventory.Beds[0].Id, Now.AddMinutes(3));
        original.RecordCheckIn(owner, Now.AddHours(1));
        var extension = CareHomeBooking.CreateExtension(original, UserId.New(), 1000m, 50m, 50m, 1, Now.AddHours(2));
        extension.MarkPaid(8401, Now.AddHours(2).AddMinutes(1));
        extension.Accept(Now.AddHours(2).AddMinutes(2));
        db.Bookings.Add(extension);
        await db.SaveChangesAsync();

        var result = await new RecordCareHomeCheckOutHandler(db, new NoopPaymob())
            .Handle(new RecordCareHomeCheckOutCommand(owner, original.Id, Now.AddHours(3)), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(CareHomeBookingStatus.RefundInitiated, extension.Status);
        Assert.Equal(CareHomeRefundStatus.Initiated, extension.RefundStatus);
        Assert.Equal(extension.TotalAmount, extension.RefundAmount);
    }

    [Theory]
    [InlineData(CareHomeAllocationMode.Private)]
    [InlineData(CareHomeAllocationMode.Suite)]
    public async Task Private_and_suite_assignment_without_bed_reads_back_and_removes_room_availability(CareHomeAllocationMode mode)
    {
        await using var db = CreateDb();
        var owner = UserId.New();
        var facility = AddFacility(db, owner);
        var inventory = AddInventory(db, facility.Id, mode, 2);
        var booking = AddAcceptedBooking(db, facility.Id, inventory.Type.Id, FamilyId.New());
        await db.SaveChangesAsync();

        var assignment = await new AssignCareHomeBookingHandler(db, new InlineReservationGuard()).Handle(
            new(owner, booking.Id, inventory.Room.Id, null, Now), default);

        Assert.True(assignment.IsSuccess);
        Assert.Equal(inventory.Room.Id, assignment.Value.AssignedRoomId);
        Assert.Null(assignment.Value.AssignedBedId);

        var operational = await new OwnerCareHomeBookingOperationalHandler(db).Handle(
            new GetOwnerCareHomeBookingOperationalQuery(owner, booking.Id, Now), default);
        Assert.True(operational.IsSuccess);
        Assert.Equal(inventory.Room.Id, operational.Value.AssignedRoomId);
        Assert.Null(operational.Value.AssignedBedId);

        var active = await new CareHomeBookingOccupancyProvider(db).GetActiveAsync(facility.Id, default);
        var availability = CareHomeAvailabilityCalculator.Calculate(
            new[] { inventory.Type }, new[] { inventory.Room }, inventory.Beds, [], active, [],
            booking.StartDate, booking.EndDate).Single();

        Assert.Contains(active, x => x.ResourceKind == CareHomeResourceKind.Room && x.ResourceId == inventory.Room.Id);
        Assert.Equal(0, availability.AvailableRooms);
        Assert.Equal(1, availability.TotalRooms);
    }

    [Theory]
    [InlineData(CareHomeAllocationMode.Private)]
    [InlineData(CareHomeAllocationMode.Suite)]
    public async Task Private_and_suite_overlapping_accepted_stay_cannot_assign_the_same_room(CareHomeAllocationMode mode)
    {
        await using var db = CreateDb();
        var owner = UserId.New();
        var facility = AddFacility(db, owner);
        var inventory = AddInventory(db, facility.Id, mode, 1);
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

    [Theory]
    [InlineData(CareHomeAllocationMode.Shared, false, 6)]
    [InlineData(CareHomeAllocationMode.Private, false, 6)]
    [InlineData(CareHomeAllocationMode.Suite, true, 7)]
    public async Task Same_type_transfer_preserves_history_and_splits_occupancy(CareHomeAllocationMode mode, bool checkInFirst, int effectiveDay)
    {
        await using var db = CreateDb();
        var owner = UserId.New();
        var facility = AddFacility(db, owner);
        var inventory = AddInventory(db, facility.Id, mode, 2);
        CareHomeRoom destinationRoom = inventory.Room;
        if (mode != CareHomeAllocationMode.Shared)
        {
            destinationRoom = CareHomeRoom.Create(facility.Id, inventory.Type.Id, "102", Now);
            db.Rooms.Add(destinationRoom);
        }
        var booking = AddAcceptedBooking(db, facility.Id, inventory.Type.Id, FamilyId.New());
        await db.SaveChangesAsync();

        Guid? sourceBed = mode == CareHomeAllocationMode.Shared ? inventory.Beds[0].Id : null;
        Guid? destinationBed = mode == CareHomeAllocationMode.Shared ? inventory.Beds[1].Id : null;
        var assignment = await new AssignCareHomeBookingHandler(db, new InlineReservationGuard())
            .Handle(new(owner, booking.Id, inventory.Room.Id, sourceBed, Now), default);
        Assert.True(assignment.IsSuccess);
        if (checkInFirst)
            Assert.True((await new RecordCareHomeCheckInHandler(db).Handle(new(owner, booking.Id, Now.AddHours(1)), default)).IsSuccess);

        var effective = new DateOnly(2026, 10, effectiveDay);
        var transferNow = new DateTime(2026, 10, 6, 6, 0, 0, DateTimeKind.Utc);
        var moved = await new TransferCareHomeBookingHandler(db, new InlineReservationGuard())
            .Handle(new(owner, booking.Id, destinationRoom.Id, destinationBed, effective, transferNow), default);

        Assert.True(moved.IsSuccess);
        Assert.Equal(effectiveDay == 6 ? destinationRoom.Id : inventory.Room.Id, moved.Value.AssignedRoomId);
        Assert.Equal(effectiveDay == 6 ? destinationBed : sourceBed, moved.Value.AssignedBedId);
        Assert.Equal(booking.RoomTypeId, moved.Value.RoomTypeId);
        Assert.Equal(booking.TotalAmount, moved.Value.TotalAmount);
        Assert.Equal(booking.BaseAmount, moved.Value.BaseAmount);
        Assert.Equal(booking.PlatformFeeAmount, moved.Value.PlatformFeeAmount);
        Assert.Equal(booking.TaxAmount, moved.Value.TaxAmount);
        Assert.Equal(1, booking.ChargeRuleVersion);
        Assert.Single(db.TransferNotificationOutbox);

        var operational = await new OwnerCareHomeBookingOperationalHandler(db)
            .Handle(new GetOwnerCareHomeBookingOperationalQuery(owner, booking.Id, transferNow), default);
        Assert.True(operational.IsSuccess);
        Assert.Equal(effectiveDay == 6 ? destinationRoom.Id : inventory.Room.Id, operational.Value.AssignedRoomId);
        Assert.Equal(effectiveDay == 6 ? destinationBed : sourceBed, operational.Value.AssignedBedId);
        Assert.Equal(2, operational.Value.AssignmentHistory!.Count);
        Assert.Equal(inventory.Room.Id, operational.Value.AssignmentHistory[1].FromRoomId);
        Assert.Equal(sourceBed, operational.Value.AssignmentHistory[1].FromBedId);
        Assert.Equal(destinationRoom.Id, operational.Value.AssignmentHistory[1].ToRoomId);
        Assert.Equal(destinationBed, operational.Value.AssignmentHistory[1].ToBedId);
        Assert.Equal(effective, operational.Value.AssignmentHistory[1].EffectiveDate);
        Assert.Equal(owner.Value, operational.Value.AssignmentHistory[1].ActorUserId);

        var active = await new CareHomeBookingOccupancyProvider(db).GetActiveAsync(facility.Id, default);
        var resourceKind = mode == CareHomeAllocationMode.Shared ? CareHomeResourceKind.Bed : CareHomeResourceKind.Room;
        var sourceId = mode == CareHomeAllocationMode.Shared ? sourceBed!.Value : inventory.Room.Id;
        var destinationId = mode == CareHomeAllocationMode.Shared ? destinationBed!.Value : destinationRoom.Id;
        Assert.Contains(active, x => x.ResourceId == sourceId && x.ResourceKind == resourceKind && x.StartDate == booking.StartDate && x.EndDate == effective);
        Assert.Contains(active, x => x.ResourceId == destinationId && x.ResourceKind == resourceKind && x.StartDate == effective && x.EndDate == booking.EndDate);
    }

    [Fact]
    public async Task Transfer_rejects_destination_already_assigned_to_overlapping_accepted_booking()
    {
        await using var db = CreateDb();
        var owner = UserId.New();
        var facility = AddFacility(db, owner);
        var inventory = AddInventory(db, facility.Id, CareHomeAllocationMode.Private, 0);
        var destination = CareHomeRoom.Create(facility.Id, inventory.Type.Id, "102", Now);
        db.Rooms.Add(destination);
        var first = AddAcceptedBooking(db, facility.Id, inventory.Type.Id, FamilyId.New());
        var second = AddAcceptedBooking(db, facility.Id, inventory.Type.Id, FamilyId.New());
        await db.SaveChangesAsync();
        var assignments = new AssignCareHomeBookingHandler(db, new InlineReservationGuard());
        Assert.True((await assignments.Handle(new(owner, first.Id, inventory.Room.Id, null, Now), default)).IsSuccess);
        Assert.True((await assignments.Handle(new(owner, second.Id, destination.Id, null, Now), default)).IsSuccess);

        var transfer = await new TransferCareHomeBookingHandler(db, new InlineReservationGuard())
            .Handle(new(owner, first.Id, destination.Id, null, new DateOnly(2026, 10, 6), Now.AddHours(2)), default);

        Assert.False(transfer.IsSuccess);
        Assert.Equal("CareHomes.Bookings.AssignmentConflict", transfer.Error.Code);
        Assert.Equal(inventory.Room.Id, first.AssignedRoomId);
        Assert.Empty(db.TransferNotificationOutbox);
    }

    [Fact]
    public async Task Legacy_assignment_without_history_keeps_its_pre_transfer_occupancy_segment()
    {
        await using var db = CreateDb();
        var owner = UserId.New();
        var facility = AddFacility(db, owner);
        var inventory = AddInventory(db, facility.Id, CareHomeAllocationMode.Private, 0);
        var destination = CareHomeRoom.Create(facility.Id, inventory.Type.Id, "102", Now);
        db.Rooms.Add(destination);
        var booking = AddAcceptedBooking(db, facility.Id, inventory.Type.Id, FamilyId.New());
        booking.AssignPhysicalResource(inventory.Room.Id, null, Now);
        await db.SaveChangesAsync();

        var effective = new DateOnly(2026, 10, 6);
        var moved = await new TransferCareHomeBookingHandler(db, new InlineReservationGuard())
            .Handle(new(owner, booking.Id, destination.Id, null, effective, new DateTime(2026, 10, 6, 6, 0, 0, DateTimeKind.Utc)), default);

        Assert.True(moved.IsSuccess);
        var history = await db.BookingAssignmentHistory.SingleAsync();
        Assert.Equal(inventory.Room.Id, history.FromRoomId);
        Assert.Equal(destination.Id, history.ToRoomId);
        var active = await new CareHomeBookingOccupancyProvider(db).GetActiveAsync(facility.Id, default);
        Assert.Contains(active, x => x.ResourceId == inventory.Room.Id && x.StartDate == booking.StartDate && x.EndDate == effective);
        Assert.Contains(active, x => x.ResourceId == destination.Id && x.StartDate == effective && x.EndDate == booking.EndDate);
    }

    [Fact]
    public async Task Transfer_rejects_backdated_out_of_stay_cross_type_and_private_bed_then_duplicate_date()
    {
        await using var db = CreateDb();
        var owner = UserId.New();
        var facility = AddFacility(db, owner);
        var inventory = AddInventory(db, facility.Id, CareHomeAllocationMode.Private, 0);
        var destination = CareHomeRoom.Create(facility.Id, inventory.Type.Id, "102", Now);
        var alternative = CareHomeRoom.Create(facility.Id, inventory.Type.Id, "103", Now);
        var invalidBed = CareHomeBed.Create(facility.Id, destination.Id, "A", Now);
        var otherType = AddInventory(db, facility.Id, CareHomeAllocationMode.Private, 0);
        var booking = AddAcceptedBooking(db, facility.Id, inventory.Type.Id, FamilyId.New());
        booking.AssignPhysicalResource(inventory.Room.Id, null, Now);
        db.Rooms.AddRange(destination, alternative);
        db.Beds.Add(invalidBed);
        await db.SaveChangesAsync();
        var handler = new TransferCareHomeBookingHandler(db, new InlineReservationGuard());
        var today = new DateTime(2026, 10, 6, 6, 0, 0, DateTimeKind.Utc);

        var backdated = await handler.Handle(new(owner, booking.Id, destination.Id, null, new DateOnly(2026, 10, 5), today), default);
        var outOfStay = await handler.Handle(new(owner, booking.Id, destination.Id, null, booking.EndDate, today), default);
        var wrongType = await handler.Handle(new(owner, booking.Id, otherType.Room.Id, null, new DateOnly(2026, 10, 6), today), default);
        var privateBed = await handler.Handle(new(owner, booking.Id, destination.Id, invalidBed.Id, new DateOnly(2026, 10, 6), today), default);
        Assert.Equal("CareHomes.Bookings.InvalidTransferDate", backdated.Error.Code);
        Assert.Equal("CareHomes.Bookings.InvalidTransferDate", outOfStay.Error.Code);
        Assert.Equal("CareHomes.Bookings.InvalidAssignment", wrongType.Error.Code);
        Assert.Equal("CareHomes.Bookings.InvalidAssignment", privateBed.Error.Code);

        var firstMove = await handler.Handle(new(owner, booking.Id, destination.Id, null, new DateOnly(2026, 10, 6), today), default);
        var repeatedDate = await handler.Handle(new(owner, booking.Id, alternative.Id, null, new DateOnly(2026, 10, 6), today), default);
        Assert.True(firstMove.IsSuccess);
        Assert.Equal("CareHomes.Bookings.TransferConflict", repeatedDate.Error.Code);
        Assert.Equal(1, await db.BookingAssignmentHistory.CountAsync(x => x.FromRoomId != null));
    }

    [Theory]
    [InlineData(CareHomeAllocationMode.Shared)]
    [InlineData(CareHomeAllocationMode.Private)]
    [InlineData(CareHomeAllocationMode.Suite)]
    public async Task Availability_readbacks_count_an_unassigned_active_booking_once(CareHomeAllocationMode mode)
    {
        await using var db = CreateDb();
        var owner = UserId.New();
        var facility = AddFacility(db, owner);
        var inventory = AddInventory(db, facility.Id, mode, 2);
        if (mode != CareHomeAllocationMode.Shared)
            db.Rooms.Add(CareHomeRoom.Create(facility.Id, inventory.Type.Id, "102", Now));
        var booking = AddAcceptedBooking(db, facility.Id, inventory.Type.Id, FamilyId.New());
        await db.SaveChangesAsync();
        var occupancy = new CareHomeBookingOccupancyProvider(db);

        var ownerResult = await new GetMyAvailabilityQueryHandler(db, occupancy).Handle(
            new(owner, booking.StartDate, booking.EndDate), default);
        var adminResult = await new GetAvailabilityQueryHandler(db, occupancy).Handle(
            new(facility.Id.Value, booking.StartDate, booking.EndDate), default);

        Assert.True(ownerResult.IsSuccess);
        Assert.True(adminResult.IsSuccess);
        foreach (var availability in new[] { ownerResult.Value.Single(), adminResult.Value.Single() })
        {
            if (mode == CareHomeAllocationMode.Shared)
                Assert.Equal(1, availability.AvailableBeds);
            else
                Assert.Equal(1, availability.AvailableRooms);
        }
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
        var checkOut = await new RecordCareHomeCheckOutHandler(db, new NoopPaymob()).Handle(new(owner, booking.Id, Now.AddHours(9)), default);
        var duplicate = await new RecordCareHomeCheckOutHandler(db, new NoopPaymob()).Handle(new(owner, booking.Id, Now.AddHours(10)), default);
        var dispute = await db.CheckInDisputes.SingleAsync();

        Assert.True(checkIn.IsSuccess);
        Assert.True(checkOut.IsSuccess);
        Assert.Equal(Now.AddHours(1), booking.ActualCheckInOnUtc);
        Assert.Equal(Now.AddHours(9), booking.ActualCheckOutOnUtc);
        Assert.Equal("CareHomes.Bookings.InvalidState", duplicate.Error.Code);
        Assert.Equal(CareHomeCheckInDisputeStatus.Open, dispute.Status);
        Assert.Equal(owner, dispute.OpenedBy);
        Assert.Equal(Now.AddHours(9), dispute.CheckoutOnUtc);

        var secondRead = await new RecordCareHomeCheckOutHandler(db, new NoopPaymob()).Handle(new(owner, booking.Id, Now.AddHours(11)), default);
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
        var checkout = await new RecordCareHomeCheckOutHandler(db, new NoopPaymob()).Handle(new(owner, booking.Id, Now.AddHours(9)), default);

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
        Assert.True((await new RecordCareHomeCheckOutHandler(db, new NoopPaymob()).Handle(new(owner, booking.Id, Now.AddHours(9)), default)).IsSuccess);
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
        Assert.True((await new RecordCareHomeCheckOutHandler(db, new NoopPaymob()).Handle(new(owner, booking.Id, Now.AddHours(9)), default)).IsSuccess);

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

        var result = await new RecordCareHomeCheckOutHandler(new FailOnceCareHomesDbContext(db), new NoopPaymob()).Handle(
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
        Assert.True((await new RecordCareHomeCheckOutHandler(db, new NoopPaymob()).Handle(new(owner, booking.Id, Now.AddHours(9)), default)).IsSuccess);

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

        var first = await new RecordCareHomeCheckOutHandler(db, new NoopPaymob()).Handle(new(owner, booking.Id, Now.AddHours(9)), default);
        var second = await new RecordCareHomeCheckOutHandler(db, new NoopPaymob()).Handle(new(owner, booking.Id, Now.AddHours(10)), default);

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
        var visible = await handler.Handle(new GetOwnerCareHomeBookingOperationalQuery(owner, booking.Id, Now), default);
        var hidden = await handler.Handle(new GetOwnerCareHomeBookingOperationalQuery(owner, otherBooking.Id, Now), default);

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

    private sealed class NoopPaymob : IPaymobClient
    {
        public Task<Result<PaymobPaymentIntent>> CreatePaymentIntentAsync(PaymobPaymentIntentInput input, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<PaymobPaymentIntent>.Failure(new("NotUsed", "Payment intent is not expected in this test.")));

        public Task<Result<string?>> RefundPaymentAsync(string paymobTransactionId, decimal amount, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<string?>.Success("test-refund"));
    }

    private sealed class FailOnceCareHomesDbContext(CareHomesDbContext inner) : ICareHomesDbContext
    {
        private bool _failed;

        public DbSet<CareHomeFacility> Facilities => inner.Facilities;
        public DbSet<CareHomeRoomType> RoomTypes => inner.RoomTypes;
        public DbSet<CareHomeRoom> Rooms => inner.Rooms;
        public DbSet<CareHomeBed> Beds => inner.Beds;
        public DbSet<CareHomeMaintenanceBlock> MaintenanceBlocks => inner.MaintenanceBlocks;
        public DbSet<CareHomeBooking> Bookings => inner.Bookings;
        public DbSet<CareHomeInternalBookingNote> InternalBookingNotes => inner.InternalBookingNotes;
        public DbSet<CareHomeRating> Ratings => inner.Ratings;
        public DbSet<CareHomePayout> Payouts => inner.Payouts;
        public DbSet<CareHomePayoutDebt> PayoutDebts => inner.PayoutDebts;
        public DbSet<CareHomeCheckInDispute> CheckInDisputes => inner.CheckInDisputes;
        public DbSet<CareHomeBookingAssignmentHistory> BookingAssignmentHistory => inner.BookingAssignmentHistory;
        public DbSet<CareHomeTransferNotificationOutbox> TransferNotificationOutbox => inner.TransferNotificationOutbox;
        public DbSet<CareHomeProfileMedia> ProfileMedia => inner.ProfileMedia;

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
        public DbSet<CareHomeInternalBookingNote> InternalBookingNotes => inner.InternalBookingNotes;
        public DbSet<CareHomeRating> Ratings => inner.Ratings;
        public DbSet<CareHomePayout> Payouts => inner.Payouts;
        public DbSet<CareHomePayoutDebt> PayoutDebts => inner.PayoutDebts;
        public DbSet<CareHomeCheckInDispute> CheckInDisputes => inner.CheckInDisputes;
        public DbSet<CareHomeBookingAssignmentHistory> BookingAssignmentHistory => inner.BookingAssignmentHistory;
        public DbSet<CareHomeTransferNotificationOutbox> TransferNotificationOutbox => inner.TransferNotificationOutbox;
        public DbSet<CareHomeProfileMedia> ProfileMedia => inner.ProfileMedia;

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
