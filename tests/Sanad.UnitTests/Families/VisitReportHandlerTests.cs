using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Families.Application.Reports;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Domain.Reports;
using Sanad.Modules.Families.Infrastructure.Persistence;

namespace Sanad.UnitTests.Families;

public sealed class VisitReportHandlerTests
{
    [Fact]
    public async Task SubmitCompletedBooking_CreatesOneReport()
    {
        using FamiliesDbContext dbContext = CreateDbContext();
        Booking booking = CreateCompletedBooking();
        dbContext.Bookings.Add(booking);
        await dbContext.SaveChangesAsync();

        var handler = new SubmitVisitReportCommandHandler(dbContext);
        var result = await handler.Handle(
            new SubmitVisitReportCommand(
                booking.CaregiverId,
                UserId.New(),
                booking.Id,
                "Observed",
                null,
                null,
                VisitReportAssessment.NoImmediateConcern,
                DateTime.UtcNow),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(booking.Id, new BookingId(result.Value.BookingId));
        Assert.Equal(1, await dbContext.VisitReports.CountAsync());
    }

    [Fact]
    public async Task SubmitTwice_ReturnsAlreadySubmitted()
    {
        using FamiliesDbContext dbContext = CreateDbContext();
        Booking booking = CreateCompletedBooking();
        dbContext.Bookings.Add(booking);
        await dbContext.SaveChangesAsync();

        var command = new SubmitVisitReportCommand(
            booking.CaregiverId,
            UserId.New(),
            booking.Id,
            "Observed",
            null,
            null,
            VisitReportAssessment.NoImmediateConcern,
            DateTime.UtcNow);
        var handler = new SubmitVisitReportCommandHandler(dbContext);

        Assert.True((await handler.Handle(command, CancellationToken.None)).IsSuccess);
        var duplicate = await handler.Handle(command, CancellationToken.None);

        Assert.False(duplicate.IsSuccess);
        Assert.Equal("Reports.Visit.AlreadySubmitted", duplicate.Error.Code);
    }

    [Fact]
    public async Task SubmitForAnotherCaregiversBooking_ReturnsNotFound()
    {
        using FamiliesDbContext dbContext = CreateDbContext();
        Booking booking = CreateCompletedBooking();
        dbContext.Bookings.Add(booking);
        await dbContext.SaveChangesAsync();

        var result = await new SubmitVisitReportCommandHandler(dbContext).Handle(
            new SubmitVisitReportCommand(
                CaregiverId.New(),
                UserId.New(),
                booking.Id,
                "Observed",
                null,
                null,
                VisitReportAssessment.NotAssessed,
                DateTime.UtcNow),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("Bookings.NotFound", result.Error.Code);
    }

    private static Booking CreateCompletedBooking()
    {
        DateTime now = DateTime.UtcNow;
        Booking booking = Booking.Create(
            FamilyId.New(),
            UserId.New(),
            ElderlyId.New(),
            CaregiverId.New(),
            BookingCaregiverType.Medical,
            BookingShiftType.HomeVisit,
            DateOnly.FromDateTime(now),
            new TimeOnly(10, 0),
            new TimeOnly(12, 0),
            "Service address",
            null,
            BookingPriceSnapshot.Calculate(500m, 15m),
            now.AddDays(1),
            DateOnly.FromDateTime(now),
            now);
        booking.MarkAsPaid("order", "transaction", now);
        booking.AcceptByCaregiver(now);
        booking.StartVisit(now);
        booking.CompleteVisit(null, now);
        return booking;
    }

    private static FamiliesDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new FamiliesDbContext(options);
    }
}
