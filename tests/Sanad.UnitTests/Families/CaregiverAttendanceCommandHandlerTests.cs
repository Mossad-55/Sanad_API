using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Families.Application.Bookings;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Infrastructure.Persistence;

namespace Sanad.UnitTests.Families;

public sealed class CaregiverAttendanceCommandHandlerTests
{
    [Fact]
    public async Task Start_ValidBooking_IsSuccessful_AndDuplicateMapsToConflict()
    {
        DateTime confirmedAt = UtcNow;
        Booking booking = CreateConfirmedBooking(confirmedAt);
        using FamiliesDbContext dbContext = CreateDbContext(booking);
        var handler = new CaregiverStartBookingCommandHandler(dbContext);

        Result started = await handler.Handle(
            new CaregiverStartBookingCommand(booking.CaregiverId, booking.Id, confirmedAt),
            CancellationToken.None);
        Result duplicate = await handler.Handle(
            new CaregiverStartBookingCommand(booking.CaregiverId, booking.Id, confirmedAt.AddMinutes(1)),
            CancellationToken.None);

        Assert.True(started.IsSuccess);
        AssertFailedWith(duplicate, "Bookings.Domain.InvalidOperation");
    }

    [Fact]
    public async Task Start_ForeignBooking_IsNotFound_AndCompleteWrongStateIsConflict()
    {
        DateTime confirmedAt = UtcNow;
        Booking booking = CreateConfirmedBooking(confirmedAt);
        using FamiliesDbContext dbContext = CreateDbContext(booking);
        var startHandler = new CaregiverStartBookingCommandHandler(dbContext);
        var completeHandler = new CaregiverCompleteBookingCommandHandler(dbContext);

        Result foreign = await startHandler.Handle(
            new CaregiverStartBookingCommand(CaregiverId.New(), booking.Id, confirmedAt),
            CancellationToken.None);
        Result wrongState = await completeHandler.Handle(
            new CaregiverCompleteBookingCommand(booking.CaregiverId, booking.Id, "not started", confirmedAt),
            CancellationToken.None);

        AssertFailedWith(foreign, "Bookings.NotFound");
        AssertFailedWith(wrongState, "Bookings.Domain.InvalidOperation");
    }

    [Fact]
    public async Task Complete_BeforeStartTime_IsRejectedWithoutPersistingCompletion()
    {
        DateTime confirmedAt = UtcNow;
        Booking booking = CreateConfirmedBooking(confirmedAt);
        using FamiliesDbContext dbContext = CreateDbContext(booking);
        var startHandler = new CaregiverStartBookingCommandHandler(dbContext);
        var completeHandler = new CaregiverCompleteBookingCommandHandler(dbContext);

        await startHandler.Handle(
            new CaregiverStartBookingCommand(booking.CaregiverId, booking.Id, confirmedAt.AddMinutes(5)),
            CancellationToken.None);
        Result result = await completeHandler.Handle(
            new CaregiverCompleteBookingCommand(booking.CaregiverId, booking.Id, "too early", confirmedAt.AddMinutes(4)),
            CancellationToken.None);

        AssertFailedWith(result, "Bookings.Domain.InvalidOperation");
        Booking stored = await dbContext.Bookings.SingleAsync(b => b.Id == booking.Id);
        Assert.Equal(BookingStatus.InProgress, stored.Status);
        Assert.Null(stored.CompletedOnUtc);
    }

    [Fact]
    public async Task Complete_ValidBooking_IsSuccessful_AndDuplicateMapsToConflict()
    {
        DateTime confirmedAt = UtcNow;
        Booking booking = CreateConfirmedBooking(confirmedAt);
        using FamiliesDbContext dbContext = CreateDbContext(booking);
        var startHandler = new CaregiverStartBookingCommandHandler(dbContext);
        var completeHandler = new CaregiverCompleteBookingCommandHandler(dbContext);

        await startHandler.Handle(
            new CaregiverStartBookingCommand(booking.CaregiverId, booking.Id, confirmedAt),
            CancellationToken.None);
        Result completed = await completeHandler.Handle(
            new CaregiverCompleteBookingCommand(booking.CaregiverId, booking.Id, "Visit completed", confirmedAt),
            CancellationToken.None);
        Result duplicate = await completeHandler.Handle(
            new CaregiverCompleteBookingCommand(booking.CaregiverId, booking.Id, "again", confirmedAt.AddMinutes(1)),
            CancellationToken.None);

        Assert.True(completed.IsSuccess);
        AssertFailedWith(duplicate, "Bookings.Domain.InvalidOperation");
    }

    private static readonly DateTime UtcNow = new(2026, 9, 20, 8, 0, 0, DateTimeKind.Utc);

    private static Booking CreateConfirmedBooking(DateTime confirmedAt)
    {
        Booking booking = Booking.Create(
            FamilyId.New(),
            UserId.New(),
            ElderlyId.New(),
            CaregiverId.New(),
            BookingCaregiverType.Medical,
            BookingShiftType.HomeVisit,
            DateOnly.FromDateTime(confirmedAt.AddDays(1)),
            new TimeOnly(10, 0),
            new TimeOnly(12, 0),
            "123 Nile St, Cairo",
            null,
            BookingPriceSnapshot.Calculate(500m, 15m),
            confirmedAt.AddHours(24),
            DateOnly.FromDateTime(confirmedAt),
            confirmedAt);

        booking.MarkAsPaid("paymob-order", "paymob-transaction", confirmedAt);
        booking.AcceptByCaregiver(confirmedAt);
        return booking;
    }

    private static FamiliesDbContext CreateDbContext(Booking booking)
    {
        var options = new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase($"sanad-attendance-{Guid.NewGuid():N}")
            .Options;
        var dbContext = new FamiliesDbContext(options);
        dbContext.Bookings.Add(booking);
        dbContext.SaveChanges();
        return dbContext;
    }

    private static void AssertFailedWith(Result result, string code)
    {
        Assert.False(result.IsSuccess);
        Assert.Equal(code, result.Error.Code);
    }
}
