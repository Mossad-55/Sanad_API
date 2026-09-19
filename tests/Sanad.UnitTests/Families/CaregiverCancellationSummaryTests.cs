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

public sealed class CaregiverCancellationSummaryTests
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
        var family = Family.Create(UserId.New(), "Cancellation Family");

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

    private static Booking ConfirmedBooking(
        Family family,
        Elderly elderly,
        CaregiverId caregiverId,
        DateTime now)
    {
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

        return booking;
    }

    private static BookingCancellationFact CreateCaregiverCancelFact(
        BookingId bookingId,
        UserId actorUserId,
        DateTime confirmedOnUtc,
        DateTime cancelledOnUtc,
        string note = "caregiver emergency")
    {
        var feedback = BookingCancellationFeedback.Create(
            BookingCancellationReasonCategory.Emergency,
            note);

        var decision = BookingCancellationPolicy.Decide(
            BookingCancellationPolicyInput.ForCaregiverCancellation(
                BookingStatus.Confirmed,
                confirmedOnUtc,
                startedOnUtc: null,
                cancelledOnUtc,
                BookingCaptureEvidence.Captured,
                feedback));

        return BookingCancellationFact.Create(bookingId, actorUserId, decision);
    }

    private static BookingCancellationFact CreateCaregiverRejectFact(
        BookingId bookingId,
        UserId actorUserId,
        DateTime cancelledOnUtc,
        string? note = "schedule conflict")
    {
        var feedback = BookingCancellationFeedback.CreateOptionalNote(note);
        var decision = BookingCancellationPolicy.Decide(
            BookingCancellationPolicyInput.ForCaregiverRejection(
                BookingStatus.PendingCaregiverApproval,
                cancelledOnUtc,
                BookingCaptureEvidence.Captured,
                feedback));

        return BookingCancellationFact.Create(bookingId, actorUserId, decision);
    }

    [Fact]
    public async Task Summary_ShouldCount_CaregiverCancelFact_AndOrderNewestFirst()
    {
        var (dbContext, family, elderly) = SeedFamily();
        DateTime now = DateTime.UtcNow;
        var caregiverId = CaregiverId.New();
        var actorUserId = UserId.New();

        var first = ConfirmedBooking(family, elderly, caregiverId, now);
        DateTime firstCancel = now.AddMinutes(2);
        first.CancelByCaregiver("ignored booking reason 1", firstCancel);
        var firstFact = CreateCaregiverCancelFact(first.Id, actorUserId, first.ConfirmedOnUtc!.Value, firstCancel, "مغلظة في الموعد");

        var second = ConfirmedBooking(family, elderly, caregiverId, now);
        DateTime secondCancel = now.AddMinutes(3);
        second.CancelByCaregiver("ignored booking reason 2", secondCancel);
        var secondFact = CreateCaregiverCancelFact(second.Id, actorUserId, second.ConfirmedOnUtc!.Value, secondCancel, "ظرف عائلي");

        var third = ConfirmedBooking(family, elderly, caregiverId, now);
        DateTime thirdCancel = now.AddMinutes(4);
        third.CancelByCaregiver("ignored booking reason 3", thirdCancel);
        var thirdFact = CreateCaregiverCancelFact(third.Id, actorUserId, third.ConfirmedOnUtc!.Value, thirdCancel, "third emergency");

        dbContext.Bookings.AddRange(first, second, third);
        dbContext.BookingCancellationFacts.AddRange(firstFact, secondFact, thirdFact);
        dbContext.SaveChanges();

        var handler = new GetCaregiverCancellationSummaryQueryHandler(dbContext);

        var result = await handler.Handle(
            new GetCaregiverCancellationSummaryQuery(caregiverId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.CancellationCount);
        Assert.Equal(3, result.Value.Recent.Count);

        // Ordered by fact.CancelledOnUtc desc: third, second, first
        Assert.Equal(third.Id.Value, result.Value.Recent[0].BookingId);
        Assert.Equal(second.Id.Value, result.Value.Recent[1].BookingId);
        Assert.Equal(first.Id.Value, result.Value.Recent[2].BookingId);

        // Reason comes from fact.ReasonNote, not booking.CancellationReason
        Assert.Equal("third emergency", result.Value.Recent[0].Reason);
        Assert.Equal("ظرف عائلي", result.Value.Recent[1].Reason);
        Assert.Equal("مغلظة في الموعد", result.Value.Recent[2].Reason);

        Assert.Equal(thirdFact.CancelledOnUtc, result.Value.Recent[0].CancelledOnUtc);
        Assert.Equal(secondFact.CancelledOnUtc, result.Value.Recent[1].CancelledOnUtc);
        Assert.Equal(firstFact.CancelledOnUtc, result.Value.Recent[2].CancelledOnUtc);

        // Booking fields from booking
        Assert.Equal(first.BookingDate, result.Value.Recent[2].BookingDate);
        Assert.Equal(first.StartTime, result.Value.Recent[2].StartTime);
        Assert.Equal(first.EndTime, result.Value.Recent[2].EndTime);
        Assert.Equal(first.ShiftType, result.Value.Recent[2].ShiftType);
    }

    [Fact]
    public async Task Summary_ShouldStillCount_AfterRefunded_Regression()
    {
        var (dbContext, family, elderly) = SeedFamily();
        DateTime now = DateTime.UtcNow;
        var caregiverId = CaregiverId.New();
        var actorUserId = UserId.New();

        var booking = ConfirmedBooking(family, elderly, caregiverId, now);
        DateTime cancelOn = now.AddMinutes(2);
        booking.CancelByCaregiver("will be refunded", cancelOn);
        var fact = CreateCaregiverCancelFact(booking.Id, actorUserId, booking.ConfirmedOnUtc!.Value, cancelOn, "refund regression note");

        dbContext.Bookings.Add(booking);
        dbContext.BookingCancellationFacts.Add(fact);
        dbContext.SaveChanges();

        // Refund flips status to Refunded — the incident must NOT vanish.
        booking.MarkRefunded("refund-txn-1", now.AddMinutes(5));
        dbContext.SaveChanges();

        var handler = new GetCaregiverCancellationSummaryQueryHandler(dbContext);

        var result = await handler.Handle(
            new GetCaregiverCancellationSummaryQuery(caregiverId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.CancellationCount);
        var item = Assert.Single(result.Value.Recent);
        Assert.Equal(booking.Id.Value, item.BookingId);
        Assert.Equal(fact.CancelledOnUtc, item.CancelledOnUtc);
        Assert.Equal("refund regression note", item.Reason);
    }

    [Fact]
    public async Task Summary_ShouldExclude_CaregiverRejectFact()
    {
        var (dbContext, family, elderly) = SeedFamily();
        DateTime now = DateTime.UtcNow;
        var caregiverId = CaregiverId.New();
        var actorUserId = UserId.New();

        // A paid booking awaiting approval that the caregiver rejects (Action = Reject, not Cancel)
        var pendingBooking = Booking.Create(
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

        pendingBooking.MarkAsPaid(pendingBooking.Id.Value.ToString(), $"txn-{Guid.NewGuid():N}", now);
        DateTime declineOn = now.AddMinutes(2);
        pendingBooking.DeclineByCaregiver("rejection note", declineOn);
        var rejectFact = CreateCaregiverRejectFact(pendingBooking.Id, actorUserId, declineOn, "rejection note");

        dbContext.Bookings.Add(pendingBooking);
        dbContext.BookingCancellationFacts.Add(rejectFact);
        dbContext.SaveChanges();

        // Also add a valid cancel fact to prove the query would count if it were Cancel
        var confirmed = ConfirmedBooking(family, elderly, caregiverId, now);
        DateTime cancelOn = now.AddMinutes(3);
        confirmed.CancelByCaregiver("valid cancel", cancelOn);
        var cancelFact = CreateCaregiverCancelFact(confirmed.Id, actorUserId, confirmed.ConfirmedOnUtc!.Value, cancelOn, "valid cancel note");
        dbContext.Bookings.Add(confirmed);
        dbContext.BookingCancellationFacts.Add(cancelFact);
        dbContext.SaveChanges();

        var handler = new GetCaregiverCancellationSummaryQueryHandler(dbContext);

        var result = await handler.Handle(
            new GetCaregiverCancellationSummaryQuery(caregiverId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        // Only the Cancel fact counted, Reject excluded
        Assert.Equal(1, result.Value.CancellationCount);
        var item = Assert.Single(result.Value.Recent);
        Assert.Equal(confirmed.Id.Value, item.BookingId);
        Assert.Equal("valid cancel note", item.Reason);
    }

    [Fact]
    public async Task Summary_ShouldExclude_LegacyBookingWithNoFact()
    {
        var (dbContext, family, elderly) = SeedFamily();
        DateTime now = DateTime.UtcNow;
        var caregiverId = CaregiverId.New();
        var actorUserId = UserId.New();

        // Legacy: status CancelledByCaregiver but NO fact
        var legacy = ConfirmedBooking(family, elderly, caregiverId, now);
        legacy.CancelByCaregiver("legacy no fact", now.AddMinutes(2));
        dbContext.Bookings.Add(legacy);
        dbContext.SaveChanges();

        // A proper fact-based cancellation for same caregiver
        var withFact = ConfirmedBooking(family, elderly, caregiverId, now);
        DateTime cancelOn = now.AddMinutes(3);
        withFact.CancelByCaregiver("with fact", cancelOn);
        var fact = CreateCaregiverCancelFact(withFact.Id, actorUserId, withFact.ConfirmedOnUtc!.Value, cancelOn, "with fact note");
        dbContext.Bookings.Add(withFact);
        dbContext.BookingCancellationFacts.Add(fact);
        dbContext.SaveChanges();

        var handler = new GetCaregiverCancellationSummaryQueryHandler(dbContext);

        var result = await handler.Handle(
            new GetCaregiverCancellationSummaryQuery(caregiverId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.CancellationCount);
        var item = Assert.Single(result.Value.Recent);
        Assert.Equal(withFact.Id.Value, item.BookingId);
        Assert.Equal("with fact note", item.Reason);
    }

    [Fact]
    public async Task Summary_ShouldExclude_OtherCaregiversCancellations()
    {
        var (dbContext, family, elderly) = SeedFamily();
        DateTime now = DateTime.UtcNow;

        var caregiverA = CaregiverId.New();
        var caregiverB = CaregiverId.New();
        var actorA = UserId.New();
        var actorB = UserId.New();

        var bookingA = ConfirmedBooking(family, elderly, caregiverA, now);
        DateTime cancelA = now.AddMinutes(2);
        bookingA.CancelByCaregiver("سبب أ", cancelA);
        var factA = CreateCaregiverCancelFact(bookingA.Id, actorA, bookingA.ConfirmedOnUtc!.Value, cancelA, "سبب أ");

        var bookingB = ConfirmedBooking(family, elderly, caregiverB, now);
        DateTime cancelB = now.AddMinutes(3);
        bookingB.CancelByCaregiver("سبب ب", cancelB);
        var factB = CreateCaregiverCancelFact(bookingB.Id, actorB, bookingB.ConfirmedOnUtc!.Value, cancelB, "سبب ب");

        dbContext.Bookings.AddRange(bookingA, bookingB);
        dbContext.BookingCancellationFacts.AddRange(factA, factB);
        dbContext.SaveChanges();

        var handler = new GetCaregiverCancellationSummaryQueryHandler(dbContext);

        var resultA = await handler.Handle(
            new GetCaregiverCancellationSummaryQuery(caregiverA),
            CancellationToken.None);

        Assert.True(resultA.IsSuccess);
        Assert.Equal(1, resultA.Value.CancellationCount);
        Assert.Equal(bookingA.Id.Value, resultA.Value.Recent.Single().BookingId);
        Assert.Equal("سبب أ", resultA.Value.Recent.Single().Reason);

        var resultB = await handler.Handle(
            new GetCaregiverCancellationSummaryQuery(caregiverB),
            CancellationToken.None);

        Assert.True(resultB.IsSuccess);
        Assert.Equal(1, resultB.Value.CancellationCount);
        Assert.Equal(bookingB.Id.Value, resultB.Value.Recent.Single().BookingId);
        Assert.Equal("سبب ب", resultB.Value.Recent.Single().Reason);
    }

    [Fact]
    public async Task Summary_ShouldExclude_NonCancelledBookings()
    {
        var (dbContext, family, elderly) = SeedFamily();
        DateTime now = DateTime.UtcNow;
        var caregiverId = CaregiverId.New();
        var actorUserId = UserId.New();

        var cancelled = ConfirmedBooking(family, elderly, caregiverId, now);
        DateTime cancelOn = now.AddMinutes(2);
        cancelled.CancelByCaregiver("لن أتمكن من الحضور", cancelOn);
        var fact = CreateCaregiverCancelFact(cancelled.Id, actorUserId, cancelled.ConfirmedOnUtc!.Value, cancelOn, "لن أتمكن من الحضور");

        var stillConfirmed = ConfirmedBooking(family, elderly, caregiverId, now);

        var pendingPayment = Booking.Create(
            family.Id,
            family.OwnerUserId,
            elderly.Id,
            caregiverId,
            BookingCaregiverType.Companion,
            BookingShiftType.Hourly,
            DateOnly.FromDateTime(now).AddDays(3),
            new TimeOnly(14, 0),
            new TimeOnly(16, 0),
            "123 Nile St, Cairo",
            null,
            BookingPriceSnapshot.Calculate(300m, 15m),
            now.AddHours(48),
            DateOnly.FromDateTime(now),
            now);

        dbContext.Bookings.AddRange(cancelled, stillConfirmed, pendingPayment);
        dbContext.BookingCancellationFacts.Add(fact);
        dbContext.SaveChanges();

        var handler = new GetCaregiverCancellationSummaryQueryHandler(dbContext);

        var result = await handler.Handle(
            new GetCaregiverCancellationSummaryQuery(caregiverId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.CancellationCount);

        var item = Assert.Single(result.Value.Recent);
        Assert.Equal(cancelled.Id.Value, item.BookingId);
    }
}
