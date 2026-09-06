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

public sealed class BookingVisibilityTests
{
    private static FamiliesDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new FamiliesDbContext(options);
    }

    private static (FamiliesDbContext Db, Family Family, Elderly Elderly) SeedFamily()
    {
        var db = CreateDbContext();
        var family = Family.Create(UserId.New(), "Visibility Family");

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

        db.Families.Add(family);
        db.Elderlies.Add(elderly);
        db.SaveChanges();

        return (db, family, elderly);
    }

    private static Booking PaidBooking(
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

        booking.RecordPaymentIntent(booking.Id.Value.ToString(), PaymentMethod.Card, now);
        booking.MarkAsPaid(booking.Id.Value.ToString(), $"txn-{Guid.NewGuid():N}", now);
        return booking;
    }

    [Fact]
    public void RefundState_ShouldMapCancelledPaid_ToFailed_AndRefunded_ToSucceeded()
    {
        DateTime now = DateTime.UtcNow;
        var family = Family.Create(UserId.New(), "X");
        var elderlyId = ElderlyId.New();
        var caregiverId = CaregiverId.New();

        var failed = Booking.Create(
            family.Id,
            family.OwnerUserId,
            elderlyId,
            caregiverId,
            BookingCaregiverType.Medical,
            BookingShiftType.HomeVisit,
            DateOnly.FromDateTime(now).AddDays(2),
            new TimeOnly(10, 0),
            new TimeOnly(12, 0),
            "addr",
            null,
            BookingPriceSnapshot.Calculate(500m, 15m),
            now.AddHours(24),
            DateOnly.FromDateTime(now),
            now);
        failed.RecordPaymentIntent(failed.Id.Value.ToString(), PaymentMethod.Card, now);
        failed.MarkAsPaid(failed.Id.Value.ToString(), "txn-1", now);
        failed.CancelByFamily("ظرف", now.AddMinutes(1));

        Assert.Equal(BookingRefundState.Failed, BookingRefundStates.Resolve(failed));

        failed.MarkRefunded("refund-1", now.AddMinutes(2));
        Assert.Equal(BookingRefundState.Succeeded, BookingRefundStates.Resolve(failed));
    }

    [Fact]
    public async Task CaregiverPastTab_ShouldInclude_FamilyCancel_And_CaregiverCancel()
    {
        var (db, family, elderly) = SeedFamily();
        DateTime now = DateTime.UtcNow;
        var caregiverId = CaregiverId.New();

        var familyCancel = PaidBooking(family, elderly, caregiverId, now);
        familyCancel.AcceptByCaregiver(now.AddMinutes(1));
        familyCancel.CancelByFamily("الغاء الأسرة", now.AddMinutes(2));

        var caregiverCancel = PaidBooking(family, elderly, caregiverId, now);
        caregiverCancel.AcceptByCaregiver(now.AddMinutes(1));
        caregiverCancel.CancelByCaregiver("الغاء مقدم الرعاية", now.AddMinutes(3));

        var otherCaregiver = PaidBooking(family, elderly, CaregiverId.New(), now);
        otherCaregiver.AcceptByCaregiver(now.AddMinutes(1));
        otherCaregiver.CancelByFamily("أخرى", now.AddMinutes(2));

        db.Bookings.AddRange(familyCancel, caregiverCancel, otherCaregiver);
        db.SaveChanges();

        var handler = new GetCaregiverBookingsQueryHandler(db);
        var result = await handler.Handle(
            new GetCaregiverBookingsQuery(caregiverId, BookingTab.Past),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.Contains(result.Value, i => i.Id == familyCancel.Id.Value
            && i.Status == BookingStatus.CancelledByFamily);
        Assert.Contains(result.Value, i => i.Id == caregiverCancel.Id.Value
            && i.Status == BookingStatus.CancelledByCaregiver);
    }

    [Fact]
    public async Task AdminList_ShouldSeparate_FailedRefund_And_Refunded()
    {
        var (db, family, elderly) = SeedFamily();
        DateTime now = DateTime.UtcNow;
        var caregiverId = CaregiverId.New();

        var failed = PaidBooking(family, elderly, caregiverId, now);
        failed.DeclineByCaregiver("لا أستطيع", now.AddMinutes(1));

        var refunded = PaidBooking(family, elderly, caregiverId, now);
        refunded.CancelByFamily("الغاء", now.AddMinutes(1));
        refunded.MarkRefunded("refund-ok", now.AddMinutes(2));

        var unpaidCancel = Booking.Create(
            family.Id,
            family.OwnerUserId,
            elderly.Id,
            caregiverId,
            BookingCaregiverType.Medical,
            BookingShiftType.HomeVisit,
            DateOnly.FromDateTime(now).AddDays(3),
            new TimeOnly(14, 0),
            new TimeOnly(16, 0),
            "addr",
            null,
            BookingPriceSnapshot.Calculate(300m, 15m),
            now.AddHours(24),
            DateOnly.FromDateTime(now),
            now);
        unpaidCancel.CancelByFamily("قبل الدفع", now.AddMinutes(1));

        db.Bookings.AddRange(failed, refunded, unpaidCancel);
        db.SaveChanges();

        var handler = new ListAdminBookingsQueryHandler(db);

        var failedResult = await handler.Handle(
            new ListAdminBookingsQuery(1, 10, AdminBookingFinanceFilter.FailedRefund),
            CancellationToken.None);
        Assert.True(failedResult.IsSuccess);
        Assert.Equal(1, failedResult.Value.TotalCount);
        Assert.Equal(failed.Id.Value, failedResult.Value.Items.Single().Id);
        Assert.Equal(BookingRefundState.Failed, failedResult.Value.Items.Single().RefundState);

        var refundedResult = await handler.Handle(
            new ListAdminBookingsQuery(1, 10, AdminBookingFinanceFilter.Refunded),
            CancellationToken.None);
        Assert.True(refundedResult.IsSuccess);
        Assert.Equal(refunded.Id.Value, refundedResult.Value.Items.Single().Id);
        Assert.Equal(BookingRefundState.Succeeded, refundedResult.Value.Items.Single().RefundState);

        var cancelledResult = await handler.Handle(
            new ListAdminBookingsQuery(1, 10, AdminBookingFinanceFilter.Cancelled),
            CancellationToken.None);
        Assert.True(cancelledResult.IsSuccess);
        Assert.Equal(2, cancelledResult.Value.TotalCount);

        var allResult = await handler.Handle(
            new ListAdminBookingsQuery(1, 10, AdminBookingFinanceFilter.All),
            CancellationToken.None);
        Assert.True(allResult.IsSuccess);
        Assert.Equal(3, allResult.Value.TotalCount);
    }
}
