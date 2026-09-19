using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Families.Application.Bookings;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Infrastructure.Persistence;
using Xunit;

namespace Sanad.UnitTests.Families;

public sealed class AdminCancellationsHistoryTests
{
    private static FamiliesDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new FamiliesDbContext(options);
    }

    private static (FamiliesDbContext DbContext, Family Family, Elderly Elderly) SeedFamily()
    {
        var dbContext = CreateDbContext();
        var family = Family.Create(UserId.New(), "History Family");

        var elderly = Elderly.Create(
            family.OwnerUserId,
            UserId.New(),
            family.Id,
            FamilyRelationshipType.Grandfather,
            FullName.Create("مسن تجريبي"),
            FullName.Create("Elderly Test"),
            Gender.Male,
            DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-70)),
            DateOnly.FromDateTime(DateTime.UtcNow));

        dbContext.Families.Add(family);
        dbContext.Elderlies.Add(elderly);
        dbContext.SaveChanges();

        return (dbContext, family, elderly);
    }

    private static Booking CreateConfirmedBooking(Family family, Elderly elderly, CaregiverId caregiverId, DateTime now, BookingShiftType shiftType = BookingShiftType.HomeVisit)
    {
        var booking = Booking.Create(
            family.Id,
            family.OwnerUserId,
            elderly.Id,
            caregiverId,
            BookingCaregiverType.Medical,
            shiftType,
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
        return booking;
    }

    private static Booking CreatePendingApprovalBooking(Family family, Elderly elderly, CaregiverId caregiverId, DateTime now)
    {
        var booking = Booking.Create(
            family.Id,
            family.OwnerUserId,
            elderly.Id,
            caregiverId,
            BookingCaregiverType.Medical,
            BookingShiftType.HomeVisit,
            DateOnly.FromDateTime(now).AddDays(3),
            new TimeOnly(14, 0),
            new TimeOnly(16, 0),
            "123 Nile St, Cairo",
            null,
            BookingPriceSnapshot.Calculate(400m, 15m),
            now.AddHours(48),
            DateOnly.FromDateTime(now),
            now);

        booking.MarkAsPaid(booking.Id.Value.ToString(), $"txn-{Guid.NewGuid():N}", now);
        return booking;
    }

    private static BookingCancellationFact CreateCaregiverCancelFact(BookingId bookingId, UserId actorUserId, DateTime confirmedOnUtc, DateTime cancelledOnUtc, BookingCancellationReasonCategory category = BookingCancellationReasonCategory.Emergency, string note = "caregiver emergency")
    {
        var feedback = BookingCancellationFeedback.Create(category, note);
        var decision = BookingCancellationPolicy.Decide(
            BookingCancellationPolicyInput.ForCaregiverCancellation(
                BookingStatus.Confirmed,
                confirmedOnUtc,
                null,
                cancelledOnUtc,
                BookingCaptureEvidence.Captured,
                feedback));
        return BookingCancellationFact.Create(bookingId, actorUserId, decision);
    }

    private static BookingCancellationFact CreateFamilyCancelWithinGraceFact(BookingId bookingId, UserId actorUserId, DateTime confirmedOnUtc, DateTime cancelledOnUtc, string note = "family within grace")
    {
        var feedback = BookingCancellationFeedback.Create(BookingCancellationReasonCategory.MedicalIssues, note);
        var decision = BookingCancellationPolicy.Decide(
            BookingCancellationPolicyInput.ForFamilyCancellation(
                BookingStatus.Confirmed,
                confirmedOnUtc,
                null,
                cancelledOnUtc,
                BookingCaptureEvidence.Captured,
                feedback));
        return BookingCancellationFact.Create(bookingId, actorUserId, decision);
    }

    private static BookingCancellationFact CreateFamilyCancelOutsideGraceFact(BookingId bookingId, UserId actorUserId, DateTime confirmedOnUtc, DateTime cancelledOnUtc)
    {
        var feedback = BookingCancellationFeedback.Create(BookingCancellationReasonCategory.Other, "family outside grace note");
        var decision = BookingCancellationPolicy.Decide(
            BookingCancellationPolicyInput.ForFamilyCancellation(
                BookingStatus.Confirmed,
                confirmedOnUtc,
                null,
                cancelledOnUtc,
                BookingCaptureEvidence.Captured,
                feedback));
        return BookingCancellationFact.Create(bookingId, actorUserId, decision);
    }

    private static BookingCancellationFact CreateFamilyCancelBeforeAcceptanceFact(BookingId bookingId, UserId actorUserId, DateTime cancelledOnUtc)
    {
        var feedback = BookingCancellationFeedback.CreateOptionalNote("  family note before acceptance  ");
        var decision = BookingCancellationPolicy.Decide(
            BookingCancellationPolicyInput.ForFamilyCancellation(
                BookingStatus.PendingCaregiverApproval,
                null,
                null,
                cancelledOnUtc,
                BookingCaptureEvidence.Captured,
                feedback));
        return BookingCancellationFact.Create(bookingId, actorUserId, decision);
    }

    [Fact]
    public async Task History_ShouldList_FamilyAndCaregiverFacts_OrderedByCancelledOnUtcDesc()
    {
        var (dbContext, family, elderly) = SeedFamily();
        DateTime now = DateTime.UtcNow;
        var caregiverId = CaregiverId.New();
        var familyActor = family.OwnerUserId;
        var caregiverActor = UserId.New();

        var booking1 = CreateConfirmedBooking(family, elderly, caregiverId, now);
        DateTime cancel1 = now.AddMinutes(2);
        booking1.CancelByFamily("family cancel", cancel1);
        var familyFact = CreateFamilyCancelWithinGraceFact(booking1.Id, familyActor, booking1.ConfirmedOnUtc!.Value, cancel1);

        var booking2 = CreateConfirmedBooking(family, elderly, caregiverId, now);
        DateTime cancel2 = now.AddMinutes(5);
        booking2.CancelByCaregiver("caregiver cancel", cancel2);
        var caregiverFact = CreateCaregiverCancelFact(booking2.Id, caregiverActor, booking2.ConfirmedOnUtc!.Value, cancel2, note: "caregiver later");

        dbContext.Bookings.AddRange(booking1, booking2);
        dbContext.BookingCancellationFacts.AddRange(familyFact, caregiverFact);
        dbContext.SaveChanges();

        var handler = new ListAdminCancellationsQueryHandler(dbContext);

        var result = await handler.Handle(new ListAdminCancellationsQuery(1, 10, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.TotalCount);
        Assert.Equal(2, result.Value.Items.Count);

        // Ordered by fact.CancelledOnUtc desc: caregiverFact (later) first
        Assert.Equal(caregiverFact.BookingId.Value, result.Value.Items[0].BookingId);
        Assert.Equal(familyFact.BookingId.Value, result.Value.Items[1].BookingId);
        Assert.Equal(caregiverFact.CancelledOnUtc, result.Value.Items[0].CancelledOnUtc);
        Assert.Equal(familyFact.CancelledOnUtc, result.Value.Items[1].CancelledOnUtc);
    }

    [Fact]
    public async Task History_ShouldFilter_ByActor()
    {
        var (dbContext, family, elderly) = SeedFamily();
        DateTime now = DateTime.UtcNow;
        var caregiverId = CaregiverId.New();
        var familyActor = family.OwnerUserId;
        var caregiverActor = UserId.New();

        var familyBooking = CreateConfirmedBooking(family, elderly, caregiverId, now);
        DateTime familyCancel = now.AddMinutes(2);
        familyBooking.CancelByFamily("family", familyCancel);
        var familyFact = CreateFamilyCancelWithinGraceFact(familyBooking.Id, familyActor, familyBooking.ConfirmedOnUtc!.Value, familyCancel);

        var caregiverBooking = CreateConfirmedBooking(family, elderly, caregiverId, now);
        DateTime caregiverCancel = now.AddMinutes(3);
        caregiverBooking.CancelByCaregiver("caregiver", caregiverCancel);
        var caregiverFact = CreateCaregiverCancelFact(caregiverBooking.Id, caregiverActor, caregiverBooking.ConfirmedOnUtc!.Value, caregiverCancel);

        dbContext.Bookings.AddRange(familyBooking, caregiverBooking);
        dbContext.BookingCancellationFacts.AddRange(familyFact, caregiverFact);
        dbContext.SaveChanges();

        var handler = new ListAdminCancellationsQueryHandler(dbContext);

        var familyOnly = await handler.Handle(new ListAdminCancellationsQuery(1, 10, BookingCancellationActorSide.Family), CancellationToken.None);
        Assert.True(familyOnly.IsSuccess);
        Assert.Equal(1, familyOnly.Value.TotalCount);
        Assert.Single(familyOnly.Value.Items);
        Assert.Equal(BookingCancellationActorSide.Family, familyOnly.Value.Items[0].ActorSide);
        Assert.Equal(familyBooking.Id.Value, familyOnly.Value.Items[0].BookingId);

        var caregiverOnly = await handler.Handle(new ListAdminCancellationsQuery(1, 10, BookingCancellationActorSide.Caregiver), CancellationToken.None);
        Assert.True(caregiverOnly.IsSuccess);
        Assert.Equal(1, caregiverOnly.Value.TotalCount);
        Assert.Single(caregiverOnly.Value.Items);
        Assert.Equal(BookingCancellationActorSide.Caregiver, caregiverOnly.Value.Items[0].ActorSide);
        Assert.Equal(caregiverBooking.Id.Value, caregiverOnly.Value.Items[0].BookingId);

        var all = await handler.Handle(new ListAdminCancellationsQuery(1, 10, null), CancellationToken.None);
        Assert.Equal(2, all.Value.TotalCount);
        Assert.Equal(2, all.Value.Items.Count);
    }

    [Fact]
    public async Task History_Paging_ShouldReturnRemainder_AndClampPageSize()
    {
        var (dbContext, family, elderly) = SeedFamily();
        DateTime now = DateTime.UtcNow;
        var caregiverId = CaregiverId.New();
        var actor = UserId.New();

        // Create 12 bookings + facts with distinct CancelledOnUtc
        var bookings = new List<Booking>();
        var facts = new List<BookingCancellationFact>();
        for (int i = 0; i < 12; i++)
        {
            var booking = CreateConfirmedBooking(family, elderly, caregiverId, now.AddMinutes(i));
            DateTime cancelOn = now.AddMinutes(10 + i);
            // Alternate cancel type to vary but all caregiver cancel for simplicity
            booking.CancelByCaregiver($"cancel {i}", cancelOn);
            var fact = CreateCaregiverCancelFact(booking.Id, actor, booking.ConfirmedOnUtc!.Value, cancelOn, note: $"note {i:D2}");
            bookings.Add(booking);
            facts.Add(fact);
        }

        dbContext.Bookings.AddRange(bookings);
        dbContext.BookingCancellationFacts.AddRange(facts);
        dbContext.SaveChanges();

        var handler = new ListAdminCancellationsQueryHandler(dbContext);

        var page1 = await handler.Handle(new ListAdminCancellationsQuery(1, 10, null), CancellationToken.None);
        Assert.True(page1.IsSuccess);
        Assert.Equal(12, page1.Value.TotalCount);
        Assert.Equal(10, page1.Value.Items.Count);
        Assert.Equal(1, page1.Value.Page);
        Assert.Equal(10, page1.Value.PageSize);

        var page2 = await handler.Handle(new ListAdminCancellationsQuery(2, 10, null), CancellationToken.None);
        Assert.True(page2.IsSuccess);
        Assert.Equal(12, page2.Value.TotalCount);
        Assert.Equal(2, page2.Value.Items.Count); // remainder
        Assert.Equal(2, page2.Value.Page);

        // PageSize clamp >100 -> 10
        var clamped = await handler.Handle(new ListAdminCancellationsQuery(1, 200, null), CancellationToken.None);
        Assert.True(clamped.IsSuccess);
        Assert.Equal(10, clamped.Value.PageSize);
        Assert.Equal(10, clamped.Value.Items.Count);
        Assert.Equal(12, clamped.Value.TotalCount);

        // Page <1 clamp -> 1
        var pageZero = await handler.Handle(new ListAdminCancellationsQuery(0, 10, null), CancellationToken.None);
        Assert.Equal(1, pageZero.Value.Page);

        // PageSize <1 clamp -> 10
        var sizeZero = await handler.Handle(new ListAdminCancellationsQuery(1, 0, null), CancellationToken.None);
        Assert.Equal(10, sizeZero.Value.PageSize);
    }

    [Fact]
    public async Task History_ShouldMapFields_AndRefundStateFactAware()
    {
        var (dbContext, family, elderly) = SeedFamily();
        DateTime now = new(2026, 3, 10, 9, 0, 0, DateTimeKind.Utc);
        var caregiverId = CaregiverId.New();
        var caregiverActor = UserId.New();
        var familyActor = family.OwnerUserId;

        // Caregiver cancel after acceptance: FullCapturedRefund, IsCaregiverIncident true, category Emergency
        var bookingCaregiver = CreateConfirmedBooking(family, elderly, caregiverId, now);
        DateTime caregiverCancel = now.AddMinutes(10);
        bookingCaregiver.CancelByCaregiver("  caregiver note needs trim  ", caregiverCancel);
        var caregiverFact = CreateCaregiverCancelFact(bookingCaregiver.Id, caregiverActor, bookingCaregiver.ConfirmedOnUtc!.Value, caregiverCancel, BookingCancellationReasonCategory.Emergency, "  caregiver emergency note  ");

        // Family cancel outside grace: NoRefundDue, IsCaregiverIncident false, category Other, should show NoRefundDue not Failed
        var bookingFamilyOutside = CreateConfirmedBooking(family, elderly, caregiverId, now);
        DateTime familyOutsideCancel = now.AddMinutes(70); // outside grace
        bookingFamilyOutside.CancelByFamily("family outside", familyOutsideCancel);
        var familyOutsideFact = CreateFamilyCancelOutsideGraceFact(bookingFamilyOutside.Id, familyActor, bookingFamilyOutside.ConfirmedOnUtc!.Value, familyOutsideCancel);

        // Family cancel before acceptance: optional note, category null, FullCapturedRefund but before acceptance
        var pendingBooking = CreatePendingApprovalBooking(family, elderly, caregiverId, now);
        DateTime beforeAcceptCancel = now.AddMinutes(5);
        pendingBooking.CancelByFamily("before acceptance note", beforeAcceptCancel);
        var beforeAcceptFact = CreateFamilyCancelBeforeAcceptanceFact(pendingBooking.Id, familyActor, beforeAcceptCancel);

        dbContext.Bookings.AddRange(bookingCaregiver, bookingFamilyOutside, pendingBooking);
        dbContext.BookingCancellationFacts.AddRange(caregiverFact, familyOutsideFact, beforeAcceptFact);
        dbContext.SaveChanges();

        var handler = new ListAdminCancellationsQueryHandler(dbContext);

        var result = await handler.Handle(new ListAdminCancellationsQuery(1, 10, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.TotalCount);

        // Find each item by bookingId
        var caregiverItem = result.Value.Items.Single(i => i.BookingId == bookingCaregiver.Id.Value);
        Assert.Equal(bookingCaregiver.BookingDate, caregiverItem.BookingDate);
        Assert.Equal(bookingCaregiver.StartTime, caregiverItem.StartTime);
        Assert.Equal(bookingCaregiver.EndTime, caregiverItem.EndTime);
        Assert.Equal(bookingCaregiver.ShiftType, caregiverItem.ShiftType);
        Assert.Equal(bookingCaregiver.Status, caregiverItem.CurrentStatus);
        Assert.Equal(BookingCancellationActorSide.Caregiver, caregiverItem.ActorSide);
        Assert.Equal(caregiverActor.Value, caregiverItem.ActorUserId);
        Assert.Equal(BookingCancellationAction.Cancel, caregiverItem.Action);
        Assert.Equal(BookingStatus.Confirmed, caregiverItem.StatusAtCancellation);
        Assert.Equal(caregiverFact.CancelledOnUtc, caregiverItem.CancelledOnUtc);
        Assert.Equal("Emergency", caregiverItem.ReasonCategory); // enum name string
        Assert.Equal("caregiver emergency note", caregiverItem.ReasonNote); // trimmed
        Assert.Equal(BookingRefundEntitlement.FullCapturedRefund, caregiverItem.RefundEntitlement);
        Assert.Equal(BookingRefundDecisionReason.CaregiverCancellationAfterAcceptance, caregiverItem.RefundDecisionReason);
        Assert.True(caregiverItem.IsCaregiverIncident);
        Assert.Equal(BookingRefundState.Failed, caregiverItem.RefundState); // paid cancelled not refunded -> Failed

        var outsideItem = result.Value.Items.Single(i => i.BookingId == bookingFamilyOutside.Id.Value);
        Assert.Equal("Other", outsideItem.ReasonCategory);
        Assert.Equal("family outside grace note", outsideItem.ReasonNote);
        Assert.Equal(BookingRefundEntitlement.NoRefundDue, outsideItem.RefundEntitlement);
        Assert.Equal(BookingRefundDecisionReason.FamilyCancellationOutsideGraceWindow, outsideItem.RefundDecisionReason);
        Assert.False(outsideItem.IsCaregiverIncident);
        // Must be NoRefundDue, not Failed — the defect 2
        Assert.Equal(BookingRefundState.NoRefundDue, outsideItem.RefundState);
        Assert.NotEqual(BookingRefundState.Failed, outsideItem.RefundState);

        var beforeItem = result.Value.Items.Single(i => i.BookingId == pendingBooking.Id.Value);
        Assert.Null(beforeItem.ReasonCategory); // before acceptance, no category
        Assert.Equal("family note before acceptance", beforeItem.ReasonNote); // trimmed
        Assert.Equal(BookingRefundEntitlement.FullCapturedRefund, beforeItem.RefundEntitlement);
        Assert.Equal(BookingRefundDecisionReason.FamilyCancellationBeforeAcceptance, beforeItem.RefundDecisionReason);
        Assert.False(beforeItem.IsCaregiverIncident);
    }

    [Fact]
    public async Task History_ShouldExclude_BookingsWithoutFact()
    {
        var (dbContext, family, elderly) = SeedFamily();
        DateTime now = DateTime.UtcNow;
        var caregiverId = CaregiverId.New();
        var actor = UserId.New();

        var withFact = CreateConfirmedBooking(family, elderly, caregiverId, now);
        DateTime cancelWith = now.AddMinutes(2);
        withFact.CancelByCaregiver("with fact", cancelWith);
        var fact = CreateCaregiverCancelFact(withFact.Id, actor, withFact.ConfirmedOnUtc!.Value, cancelWith);

        var withoutFact = CreateConfirmedBooking(family, elderly, caregiverId, now);
        withoutFact.CancelByCaregiver("without fact legacy", now.AddMinutes(3));
        // No fact for withoutFact

        dbContext.Bookings.AddRange(withFact, withoutFact);
        dbContext.BookingCancellationFacts.Add(fact);
        dbContext.SaveChanges();

        var handler = new ListAdminCancellationsQueryHandler(dbContext);

        var result = await handler.Handle(new ListAdminCancellationsQuery(1, 10, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.TotalCount);
        Assert.Single(result.Value.Items);
        Assert.Equal(withFact.Id.Value, result.Value.Items[0].BookingId);
        Assert.DoesNotContain(result.Value.Items, i => i.BookingId == withoutFact.Id.Value);
    }
}
