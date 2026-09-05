using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Families.Domain.Bookings;
using Xunit;

namespace Sanad.UnitTests.Families;

public sealed class BookingTests
{
    [Fact]
    public void BookingLifecycle_StateTransitions_ExecuteCorrectly()
    {
        DateTime now = DateTime.UtcNow;
        DateOnly today = DateOnly.FromDateTime(now);

        BookingPriceSnapshot price = BookingPriceSnapshot.Calculate(500m, 15.00m);

        Booking booking = Booking.Create(
            FamilyId.New(),
            UserId.New(),
            ElderlyId.New(),
            CaregiverId.New(),
            BookingCaregiverType.Medical,
            BookingShiftType.HomeVisit,
            today.AddDays(1),
            new TimeOnly(10, 0),
            new TimeOnly(12, 0),
            "123 Nile St, Cairo",
            "Please bring blood pressure cuff",
            price,
            now.AddHours(24),
            today,
            now);

        Assert.Equal(BookingStatus.PendingPayment, booking.Status);

        // 1. Pay
        booking.MarkAsPaid("paymob_123", "txn_456", now);
        Assert.Equal(BookingStatus.PendingCaregiverApproval, booking.Status);

        // 2. Accept
        booking.AcceptByCaregiver(now);
        Assert.Equal(BookingStatus.Confirmed, booking.Status);

        // 3. Start visit
        booking.StartVisit(now);
        Assert.Equal(BookingStatus.InProgress, booking.Status);

        // 4. Complete visit
        booking.CompleteVisit("Patient took meds and blood pressure was 120/80.", now);
        Assert.Equal(BookingStatus.Completed, booking.Status);
        Assert.Equal("Patient took meds and blood pressure was 120/80.", booking.CaregiverNotes);
    }
}