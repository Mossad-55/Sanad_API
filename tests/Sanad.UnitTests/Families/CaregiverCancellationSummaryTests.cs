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

    [Fact]
    public async Task Summary_ShouldCount_AndOrderNewestFirst()
    {
        var (dbContext, family, elderly) = SeedFamily();
        DateTime now = DateTime.UtcNow;
        var caregiverId = CaregiverId.New();

        var first = ConfirmedBooking(family, elderly, caregiverId, now);
        first.CancelByCaregiver("مغلظة في الموعد", now.AddMinutes(2));

        var second = ConfirmedBooking(family, elderly, caregiverId, now);
        second.CancelByCaregiver("ظرف عائلي", now.AddMinutes(3));

        var third = ConfirmedBooking(family, elderly, caregiverId, now);
        third.CancelByCaregiver(null!, now.AddMinutes(4));

        dbContext.Bookings.AddRange(first, second, third);
        dbContext.SaveChanges();

        var handler = new GetCaregiverCancellationSummaryQueryHandler(dbContext);

        var result = await handler.Handle(
            new GetCaregiverCancellationSummaryQuery(caregiverId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.CancellationCount);
        Assert.Equal(3, result.Value.Recent.Count);

        Assert.Equal(third.Id.Value, result.Value.Recent[0].BookingId);
        Assert.Equal(second.Id.Value, result.Value.Recent[1].BookingId);
        Assert.Equal(first.Id.Value, result.Value.Recent[2].BookingId);

        Assert.Equal("مغلظة في الموعد", result.Value.Recent[2].Reason);
        Assert.Null(result.Value.Recent[0].Reason);
    }

    [Fact]
    public async Task Summary_ShouldExclude_NonCancelledBookings()
    {
        var (dbContext, family, elderly) = SeedFamily();
        DateTime now = DateTime.UtcNow;
        var caregiverId = CaregiverId.New();

        var cancelled = ConfirmedBooking(family, elderly, caregiverId, now);
        cancelled.CancelByCaregiver("لن أتمكن من الحضور", now.AddMinutes(2));

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

    [Fact]
    public async Task Summary_ShouldExclude_OtherCaregiversCancellations()
    {
        var (dbContext, family, elderly) = SeedFamily();
        DateTime now = DateTime.UtcNow;

        var caregiverA = CaregiverId.New();
        var caregiverB = CaregiverId.New();

        var bookingA = ConfirmedBooking(family, elderly, caregiverA, now);
        bookingA.CancelByCaregiver("سبب أ", now.AddMinutes(2));

        var bookingB = ConfirmedBooking(family, elderly, caregiverB, now);
        bookingB.CancelByCaregiver("سبب ب", now.AddMinutes(3));

        dbContext.Bookings.AddRange(bookingA, bookingB);
        dbContext.SaveChanges();

        var handler = new GetCaregiverCancellationSummaryQueryHandler(dbContext);

        var resultA = await handler.Handle(
            new GetCaregiverCancellationSummaryQuery(caregiverA),
            CancellationToken.None);

        Assert.True(resultA.IsSuccess);
        Assert.Equal(1, resultA.Value.CancellationCount);
        Assert.Equal(bookingA.Id.Value, resultA.Value.Recent.Single().BookingId);

        var resultB = await handler.Handle(
            new GetCaregiverCancellationSummaryQuery(caregiverB),
            CancellationToken.None);

        Assert.True(resultB.IsSuccess);
        Assert.Equal(1, resultB.Value.CancellationCount);
        Assert.Equal(bookingB.Id.Value, resultB.Value.Recent.Single().BookingId);
    }
}