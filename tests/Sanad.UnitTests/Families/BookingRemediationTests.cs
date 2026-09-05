using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Families.Application.Abstractions.Caregivers;
using Sanad.Modules.Families.Application.Abstractions.Payments;
using Sanad.Modules.Families.Application.Bookings;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Infrastructure.Persistence;
using Xunit;

namespace Sanad.UnitTests.Families;

public sealed class BookingRemediationTests
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
        var family = Family.Create(UserId.New(), "Booking Family");

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

    private sealed class FakePricing(BookingCaregiverType type, decimal fee) : ICaregiverBookingPricing
    {
        public Task<Result<CaregiverBookingPrice>> GetBookingPriceAsync(
            CaregiverId caregiverId,
            BookingShiftType shiftType,
            TimeOnly startTime,
            TimeOnly endTime,
            CancellationToken cancellationToken = default)
        {
            if (fee < 0m)
            {
                return Task.FromResult(Result<CaregiverBookingPrice>.Failure(
                    new Error("Caregivers.Discovery.QuoteNotAvailable", "Caregiver pricing is not available.")));
            }

            return Task.FromResult(Result<CaregiverBookingPrice>.Success(
                new CaregiverBookingPrice(type, fee)));
        }
    }

    private sealed class StubPaymobClient : IPaymobClient
    {
        public Task<Result<PaymobPaymentIntent>> CreatePaymentIntentAsync(
            PaymobPaymentIntentInput input,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<PaymobPaymentIntent>.Success(
                new PaymobPaymentIntent(
                    input.BookingId.Value.ToString(),
                    $"dev-intention-{Guid.NewGuid():N}",
                    $"dev-secret-{Guid.NewGuid():N}",
                    "pk_dev")));
        }

        public Task<Result<string?>> RefundPaymentAsync(
            string paymobTransactionId,
            decimal amount,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<string?>.Success($"dev-refund-{Guid.NewGuid():N}"));
        }
    }

    private static CreateBookingCheckoutCommand CheckoutCommand(
        Family family,
        Elderly elderly,
        DateTime utcNow) => new(
        family.OwnerUserId,
        elderly.Id,
        CaregiverId.New(),
        BookingShiftType.HomeVisit,
        DateOnly.FromDateTime(utcNow).AddDays(2),
        new TimeOnly(10, 0),
        new TimeOnly(12, 0),
        "123 Nile St, Cairo",
        null,
        DateOnly.FromDateTime(utcNow),
        utcNow);

    [Fact]
    public void Create_ShouldRejectDeadlineAtOrBeforeCreation()
    {
        DateTime now = DateTime.UtcNow;

        Assert.ThrowsAny<Exception>(() => Booking.Create(
            FamilyId.New(), UserId.New(), ElderlyId.New(), CaregiverId.New(),
            BookingCaregiverType.Medical, BookingShiftType.HomeVisit,
            DateOnly.FromDateTime(now).AddDays(1), new TimeOnly(10, 0), new TimeOnly(12, 0),
            "Address", null, BookingPriceSnapshot.Calculate(500m, 15m),
            now, DateOnly.FromDateTime(now), now));
    }

    [Fact]
    public void AcceptByCaregiver_ShouldThrow_WhenDeadlinePassed()
    {
        DateTime now = DateTime.UtcNow;
        var booking = Booking.Create(
            FamilyId.New(), UserId.New(), ElderlyId.New(), CaregiverId.New(),
            BookingCaregiverType.Companion, BookingShiftType.Hourly,
            DateOnly.FromDateTime(now).AddDays(1), new TimeOnly(10, 0), new TimeOnly(11, 0),
            "Address", null, BookingPriceSnapshot.Calculate(500m, 15m),
            now.AddHours(24), DateOnly.FromDateTime(now), now);

        booking.MarkAsPaid("order", "txn", now);

        Assert.ThrowsAny<Exception>(() => booking.AcceptByCaregiver(now.AddHours(25)));
    }

    [Fact]
    public void Expire_ShouldExpirePendingBooking_AfterDeadline_AndRejectBeforeDeadline()
    {
        DateTime now = DateTime.UtcNow;
        var booking = Booking.Create(
            FamilyId.New(), UserId.New(), ElderlyId.New(), CaregiverId.New(),
            BookingCaregiverType.Medical, BookingShiftType.HomeVisit,
            DateOnly.FromDateTime(now).AddDays(1), new TimeOnly(10, 0), new TimeOnly(12, 0),
            "Address", null, BookingPriceSnapshot.Calculate(500m, 15m),
            now.AddHours(24), DateOnly.FromDateTime(now), now);

        Assert.ThrowsAny<Exception>(() => booking.Expire(now.AddHours(1)));

        booking.Expire(now.AddHours(25));

        Assert.Equal(BookingStatus.Expired, booking.Status);
        Assert.NotNull(booking.ExpiredOnUtc);
    }

    [Fact]
    public async Task Checkout_ShouldPriceServerSide_AndSnapshotTotals()
    {
        var (dbContext, family, elderly) = SeedFamily();
        var handler = new CreateBookingCheckoutCommandHandler(
            dbContext,
            new FakePricing(BookingCaregiverType.Medical, 2500m));

        var result = await handler.Handle(
            CheckoutCommand(family, elderly, DateTime.UtcNow),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2875m, result.Value.TotalPayableAmount);

        var booking = await dbContext.Bookings.SingleAsync();
        Assert.Equal(BookingCaregiverType.Medical, booking.CaregiverType);
        Assert.Equal(2500m, booking.PriceSnapshot.BaseCaregiverFee);
        Assert.Equal(2875m, booking.PriceSnapshot.TotalPayableAmount);
        Assert.True(booking.AcceptanceDeadlineUtc > DateTime.UtcNow);
    }

    [Fact]
    public async Task Checkout_ShouldPropagatePricingFailure()
    {
        var (dbContext, family, elderly) = SeedFamily();
        var handler = new CreateBookingCheckoutCommandHandler(
            dbContext,
            new FakePricing(BookingCaregiverType.Companion, -1m));

        var result = await handler.Handle(
            CheckoutCommand(family, elderly, DateTime.UtcNow),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("Caregivers.Discovery.QuoteNotAvailable", result.Error.Code);
    }

    [Fact]
    public async Task Cancel_ShouldRejectForeignFamilyBooking()
    {
        var (dbContext, family, elderly) = SeedFamily();

        // A second, unrelated family whose owner attempts the cross-family cancel
        var attackerFamily = Family.Create(UserId.New(), "Other Family");
        dbContext.Families.Add(attackerFamily);
        dbContext.SaveChanges();

        var checkout = new CreateBookingCheckoutCommandHandler(
            dbContext,
            new FakePricing(BookingCaregiverType.Medical, 500m));

        var checkoutResult = await checkout.Handle(
            CheckoutCommand(family, elderly, DateTime.UtcNow),
            CancellationToken.None);

        Assert.True(checkoutResult.IsSuccess);

        var handler = new CancelBookingCommandHandler(
            dbContext,
            new StubPaymobClient());

        var result = await handler.Handle(
            new CancelBookingCommand(
                new BookingId(checkoutResult.Value.BookingId),
                attackerFamily.OwnerUserId, // member of a DIFFERENT family
                "Emergency",
                DateTime.UtcNow),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("Bookings.BookingNotInFamily", result.Error.Code);
    }

    [Fact]
    public async Task Checkout_ShouldRejectSlot_WhenOnlyAPendingBookingExists()
    {
        var (dbContext, family, elderly) = SeedFamily();
        var checkout = new CreateBookingCheckoutCommandHandler(
            dbContext,
            new FakePricing(BookingCaregiverType.Medical, 500m));
        var command = CheckoutCommand(family, elderly, DateTime.UtcNow);

        var first = await checkout.Handle(command, CancellationToken.None);
        Assert.True(first.IsSuccess);

        var second = await checkout.Handle(command, CancellationToken.None);

        Assert.False(second.IsSuccess);
        Assert.Equal("Bookings.ScheduleConflict", second.Error.Code);
    }
}