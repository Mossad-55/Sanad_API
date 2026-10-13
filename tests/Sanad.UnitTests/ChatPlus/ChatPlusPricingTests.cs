using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Caregivers.Application.Discovery;
using Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;
using Sanad.Modules.Caregivers.Infrastructure.Persistence;
using Sanad.Modules.Families.Application.Abstractions.Caregivers;
using Sanad.Modules.Families.Application.Abstractions.Payments;
using Sanad.Modules.Families.Application.Bookings;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Infrastructure.Persistence;
using Sanad.UnitTests.Finance;

namespace Sanad.UnitTests.ChatPlus;

/// <summary>
/// Phase 2 pricing tests: exact pool semantics (N=5, independent nulls,
/// window boundaries, attribution, overnight rating, rounding, exclusions)
/// and checkout attribution validation. InMemory proves the calculation;
/// PostgreSQL aggregate equality needs the authorized exact-target gate
/// (Phase 7) and is recorded as such.
/// </summary>
public sealed class ChatPlusPricingTests
{
    private static readonly DateTime Now =
        DateTime.SpecifyKind(new DateTime(2026, 10, 1, 12, 0, 0), DateTimeKind.Utc);

    private static FamiliesDbContext CreateFamiliesDb() =>
        new(new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static CaregiversDbContext CreateCaregiversDb() =>
        new(new DbContextOptionsBuilder<CaregiversDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task Pools_FourHourlyPlusFiveEightHourGivesNullHourlyAndRange()
    {
        await using FamiliesDbContext families = CreateFamiliesDb();
        await using CaregiversDbContext caregivers = CreateCaregiversDb();
        (Guid areaId, Guid serviceId) = await SeedCatalogAsync(caregivers);

        for (int i = 0; i < 4; i++)
        {
            SeedCompleted(
                families, areaId, serviceId,
                BookingShiftType.Hourly, 400m,
                new TimeOnly(9, 0), new TimeOnly(17, 0),
                Now.AddDays(-10).AddHours(-i));
        }

        for (int i = 0; i < 5; i++)
        {
            SeedCompleted(
                families, areaId, serviceId,
                BookingShiftType.EightHourShift, 300m,
                new TimeOnly(9, 0), new TimeOnly(17, 0),
                Now.AddDays(-10).AddHours(-i));
        }

        await families.SaveChangesAsync();

        var handler = new GetPricingInsightsQueryHandler(
            families, caregivers, new FixedClock(Now));
        PricingInsightsResponse response = (await handler.Handle(
            new GetPricingInsightsQuery(areaId, serviceId), default)).Value;

        Assert.Equal("EGP", response.Currency);
        Assert.Null(response.MarketHourlyPrice);
        Assert.NotNull(response.MostRequestedEightHourDayPrice);
        Assert.Equal(300m, response.MostRequestedEightHourDayPrice.Min);
        Assert.Equal(300m, response.MostRequestedEightHourDayPrice.Max);
        Assert.Equal(Now, response.UpdatedOnUtc);
    }

    [Fact]
    public async Task Pools_EmptyGivesBothNullWithoutInventedNumbers()
    {
        await using FamiliesDbContext families = CreateFamiliesDb();
        await using CaregiversDbContext caregivers = CreateCaregiversDb();
        (Guid areaId, Guid serviceId) = await SeedCatalogAsync(caregivers);

        var handler = new GetPricingInsightsQueryHandler(
            families, caregivers, new FixedClock(Now));
        PricingInsightsResponse response = (await handler.Handle(
            new GetPricingInsightsQuery(areaId, serviceId), default)).Value;

        Assert.Null(response.MarketHourlyPrice);
        Assert.Null(response.MostRequestedEightHourDayPrice);
        Assert.Equal("EGP", response.Currency);
    }

    [Fact]
    public async Task Window_BoundariesAreInclusiveAndFutureExcluded()
    {
        await using FamiliesDbContext families = CreateFamiliesDb();
        await using CaregiversDbContext caregivers = CreateCaregiversDb();
        (Guid areaId, Guid serviceId) = await SeedCatalogAsync(caregivers);

        // Five in-window (one exactly on the 90-day edge) + one day too
        // old + one in the future: only the five count.
        SeedCompleted(
            families, areaId, serviceId,
            BookingShiftType.Hourly, 80m,
            new TimeOnly(9, 0), new TimeOnly(17, 0),
            Now.AddDays(-90));
        for (int i = 0; i < 4; i++)
        {
            SeedCompleted(
                families, areaId, serviceId,
                BookingShiftType.Hourly, 80m,
                new TimeOnly(9, 0), new TimeOnly(17, 0),
                Now.AddDays(-5).AddHours(-i));
        }

        SeedCompleted(
            families, areaId, serviceId,
            BookingShiftType.Hourly, 10_000m,
            new TimeOnly(9, 0), new TimeOnly(17, 0),
            Now.AddDays(-90).AddSeconds(-1));
        SeedCompleted(
            families, areaId, serviceId,
            BookingShiftType.Hourly, 10_000m,
            new TimeOnly(9, 0), new TimeOnly(17, 0),
            Now.AddHours(1));

        await families.SaveChangesAsync();

        var handler = new GetPricingInsightsQueryHandler(
            families, caregivers, new FixedClock(Now));
        PricingInsightsResponse response = (await handler.Handle(
            new GetPricingInsightsQuery(areaId, serviceId), default)).Value;

        // 80 EGP over 8h = 10.00; the 10_000 outliers must not leak in.
        Assert.NotNull(response.MarketHourlyPrice);
        Assert.Equal(10m, response.MarketHourlyPrice.Min);
        Assert.Equal(10m, response.MarketHourlyPrice.Max);
    }

    [Fact]
    public async Task Attribution_LegacyAndForeignPairsContributeNothing()
    {
        await using FamiliesDbContext families = CreateFamiliesDb();
        await using CaregiversDbContext caregivers = CreateCaregiversDb();
        (Guid areaId, Guid serviceId) = await SeedCatalogAsync(caregivers);
        Guid otherArea = Guid.CreateVersion7();

        for (int i = 0; i < 5; i++)
        {
            SeedCompleted(
                families, areaId, serviceId,
                BookingShiftType.Hourly, 160m,
                new TimeOnly(9, 0), new TimeOnly(17, 0),
                Now.AddDays(-5).AddHours(-i));
        }

        // Legacy unattributed booking + foreign-pair booking: no samples.
        SeedCompleted(
            families, null, null,
            BookingShiftType.Hourly, 1m,
            new TimeOnly(9, 0), new TimeOnly(10, 0),
            Now.AddDays(-5));
        SeedCompleted(
            families, otherArea, serviceId,
            BookingShiftType.Hourly, 1m,
            new TimeOnly(9, 0), new TimeOnly(10, 0),
            Now.AddDays(-5));

        await families.SaveChangesAsync();

        var handler = new GetPricingInsightsQueryHandler(
            families, caregivers, new FixedClock(Now));
        PricingInsightsResponse response = (await handler.Handle(
            new GetPricingInsightsQuery(areaId, serviceId), default)).Value;

        // 160 over 8h = 20.00; the 1 EGP decoys must not leak in.
        Assert.NotNull(response.MarketHourlyPrice);
        Assert.Equal(20m, response.MarketHourlyPrice.Min);
        Assert.Equal(20m, response.MarketHourlyPrice.Max);
    }

    [Fact]
    public async Task Hourly_OvernightDurationsRateByWrappedHours()
    {
        await using FamiliesDbContext families = CreateFamiliesDb();
        await using CaregiversDbContext caregivers = CreateCaregiversDb();
        (Guid areaId, Guid serviceId) = await SeedCatalogAsync(caregivers);

        // 20:00 -> 02:00 wraps to 6h; 600 base => 100.00.
        for (int i = 0; i < 5; i++)
        {
            SeedCompleted(
                families, areaId, serviceId,
                BookingShiftType.Hourly, 600m,
                new TimeOnly(20, 0), new TimeOnly(2, 0),
                Now.AddDays(-5).AddHours(-i));
        }

        await families.SaveChangesAsync();

        var handler = new GetPricingInsightsQueryHandler(
            families, caregivers, new FixedClock(Now));
        PricingInsightsResponse response = (await handler.Handle(
            new GetPricingInsightsQuery(areaId, serviceId), default)).Value;

        Assert.NotNull(response.MarketHourlyPrice);
        Assert.Equal(100m, response.MarketHourlyPrice.Min);
        Assert.Equal(100m, response.MarketHourlyPrice.Max);
    }

    [Fact]
    public async Task Hourly_RoundsHalfToEvenBeforeMinMax()
    {
        await using FamiliesDbContext families = CreateFamiliesDb();
        await using CaregiversDbContext caregivers = CreateCaregiversDb();
        (Guid areaId, Guid serviceId) = await SeedCatalogAsync(caregivers);

        // 301 over 8h = 37.625 -> banker's 37.62.
        for (int i = 0; i < 5; i++)
        {
            SeedCompleted(
                families, areaId, serviceId,
                BookingShiftType.Hourly, 301m,
                new TimeOnly(9, 0), new TimeOnly(17, 0),
                Now.AddDays(-5).AddHours(-i));
        }

        await families.SaveChangesAsync();

        var handler = new GetPricingInsightsQueryHandler(
            families, caregivers, new FixedClock(Now));
        PricingInsightsResponse response = (await handler.Handle(
            new GetPricingInsightsQuery(areaId, serviceId), default)).Value;

        Assert.NotNull(response.MarketHourlyPrice);
        Assert.Equal(37.62m, response.MarketHourlyPrice.Min);
        Assert.Equal(37.62m, response.MarketHourlyPrice.Max);
    }

    [Fact]
    public async Task Exclusions_UnpaidCancelledAndOtherShiftsNeverEnterPools()
    {
        await using FamiliesDbContext families = CreateFamiliesDb();
        await using CaregiversDbContext caregivers = CreateCaregiversDb();
        (Guid areaId, Guid serviceId) = await SeedCatalogAsync(caregivers);

        for (int i = 0; i < 5; i++)
        {
            SeedCompleted(
                families, areaId, serviceId,
                BookingShiftType.EightHourShift, 250m,
                new TimeOnly(9, 0), new TimeOnly(17, 0),
                Now.AddDays(-5).AddHours(-i));
        }

        // Unpaid hold, 12h/24h/home-visit completions: none may enter the
        // hourly pool (or move the eight-hour range).
        SeedUnpaid(families, areaId, serviceId);
        SeedCompleted(
            families, areaId, serviceId,
            BookingShiftType.TwelveHourShift, 1m,
            new TimeOnly(9, 0), new TimeOnly(21, 0),
            Now.AddDays(-5));
        SeedCompleted(
            families, areaId, serviceId,
            BookingShiftType.HomeVisit, 1m,
            new TimeOnly(9, 0), new TimeOnly(10, 0),
            Now.AddDays(-5));

        await families.SaveChangesAsync();

        var handler = new GetPricingInsightsQueryHandler(
            families, caregivers, new FixedClock(Now));
        PricingInsightsResponse response = (await handler.Handle(
            new GetPricingInsightsQuery(areaId, serviceId), default)).Value;

        Assert.Null(response.MarketHourlyPrice);
        Assert.NotNull(response.MostRequestedEightHourDayPrice);
        Assert.Equal(250m, response.MostRequestedEightHourDayPrice.Min);
        Assert.Equal(250m, response.MostRequestedEightHourDayPrice.Max);
    }

    [Fact]
    public async Task Filter_EmptyUnknownOrInactiveIdsAreInvalidFilter()
    {
        await using FamiliesDbContext families = CreateFamiliesDb();
        await using CaregiversDbContext caregivers = CreateCaregiversDb();
        (Guid areaId, Guid serviceId) = await SeedCatalogAsync(caregivers);

        var handler = new GetPricingInsightsQueryHandler(
            families, caregivers, new FixedClock(Now));

        var empty = await handler.Handle(
            new GetPricingInsightsQuery(Guid.Empty, serviceId), default);
        Assert.True(empty.IsFailure);
        Assert.Equal(PricingInsightsErrors.InvalidFilter, empty.Error);

        var unknown = await handler.Handle(
            new GetPricingInsightsQuery(Guid.CreateVersion7(), serviceId), default);
        Assert.True(unknown.IsFailure);
        Assert.Equal(PricingInsightsErrors.InvalidFilter, unknown.Error);

        var area = await caregivers.Areas.SingleAsync(a => a.Id.Value == areaId);
        area.Deactivate();
        await caregivers.SaveChangesAsync();

        var inactive = await handler.Handle(
            new GetPricingInsightsQuery(areaId, serviceId), default);
        Assert.True(inactive.IsFailure);
        Assert.Equal(PricingInsightsErrors.InvalidFilter, inactive.Error);
    }

    // ------------------------------------------------------------------
    // Checkout attribution.
    // ------------------------------------------------------------------

    [Fact]
    public void BookingAttribution_RequiresBothTogetherAndRejectsEmpties()
    {
        var snapshot = BookingPriceSnapshot.Calculate(100m, 10m, 5m, 1);

        Assert.Throws<Sanad.BuildingBlocks.Domain.Exceptions.DomainException>(() =>
            Booking.Create(
                FamilyId.New(), UserId.New(), ElderlyId.New(), CaregiverId.New(),
                BookingCaregiverType.Companion, BookingShiftType.Hourly,
                DateOnly.FromDateTime(Now), new TimeOnly(9, 0), new TimeOnly(17, 0),
                "address", null, snapshot, Now.AddHours(23), DateOnly.FromDateTime(Now), Now,
                Guid.CreateVersion7(), null));

        Assert.Throws<Sanad.BuildingBlocks.Domain.Exceptions.DomainException>(() =>
            Booking.Create(
                FamilyId.New(), UserId.New(), ElderlyId.New(), CaregiverId.New(),
                BookingCaregiverType.Companion, BookingShiftType.Hourly,
                DateOnly.FromDateTime(Now), new TimeOnly(9, 0), new TimeOnly(17, 0),
                "address", null, snapshot, Now.AddHours(23), DateOnly.FromDateTime(Now), Now,
                Guid.Empty, Guid.CreateVersion7()));

        Guid area = Guid.CreateVersion7();
        Guid service = Guid.CreateVersion7();
        Booking attributed = Booking.Create(
            FamilyId.New(), UserId.New(), ElderlyId.New(), CaregiverId.New(),
            BookingCaregiverType.Companion, BookingShiftType.Hourly,
            DateOnly.FromDateTime(Now), new TimeOnly(9, 0), new TimeOnly(17, 0),
            "address", null, snapshot, Now.AddHours(23), DateOnly.FromDateTime(Now), Now,
            area, service);

        Assert.Equal(area, attributed.BookingAreaId);
        Assert.Equal(service, attributed.BookingServiceId);

        Booking legacy = Booking.Create(
            FamilyId.New(), UserId.New(), ElderlyId.New(), CaregiverId.New(),
            BookingCaregiverType.Companion, BookingShiftType.Hourly,
            DateOnly.FromDateTime(Now), new TimeOnly(9, 0), new TimeOnly(17, 0),
            "address", null, snapshot, Now.AddHours(23), DateOnly.FromDateTime(Now), Now);

        Assert.Null(legacy.BookingAreaId);
        Assert.Null(legacy.BookingServiceId);
    }

    [Fact]
    public async Task Checkout_AttributedWithoutCatalogLookupFailsClosed()
    {
        await using FamiliesDbContext families = CreateFamiliesDb();
        (Family family, Elderly elderly) = await SeedCheckoutFamilyAsync(families);

        // catalogLookup omitted (null): an attributed checkout must fail
        // closed instead of silently persisting unverified dimensions.
        var handler = new CreateBookingCheckoutCommandHandler(
            families,
            new FakeCheckoutPricing(),
            new FixedPlatformChargeRules(15m, 0m));

        var attributed = await handler.Handle(
            new CreateBookingCheckoutCommand(
                family.OwnerUserId,
                elderly.Id,
                CaregiverId.New(),
                BookingShiftType.HomeVisit,
                DateOnly.FromDateTime(Now).AddDays(2),
                new TimeOnly(10, 0),
                new TimeOnly(12, 0),
                "123 Nile St, Cairo",
                null,
                DateOnly.FromDateTime(Now),
                Now,
                Guid.CreateVersion7(),
                Guid.CreateVersion7()),
            default);

        Assert.True(attributed.IsFailure);
        Assert.Equal("Bookings.AttributionInvalid", attributed.Error.Code);
        Assert.Equal(0, await families.Bookings.CountAsync());

        // Legacy omission still checks out without the seam.
        var legacy = await handler.Handle(
            new CreateBookingCheckoutCommand(
                family.OwnerUserId,
                elderly.Id,
                CaregiverId.New(),
                BookingShiftType.HomeVisit,
                DateOnly.FromDateTime(Now).AddDays(2),
                new TimeOnly(10, 0),
                new TimeOnly(12, 0),
                "123 Nile St, Cairo",
                null,
                DateOnly.FromDateTime(Now),
                Now),
            default);

        Assert.True(legacy.IsSuccess);
    }

    // ------------------------------------------------------------------
    // Helpers.
    // ------------------------------------------------------------------

    private static async Task<(Family, Elderly)> SeedCheckoutFamilyAsync(
        FamiliesDbContext families)
    {
        var family = Family.Create(UserId.New(), "Pricing Family");
        var elderly = Elderly.Create(
            family.OwnerUserId,
            UserId.New(),
            family.Id,
            FamilyRelationshipType.Grandfather,
            FullName.Create("مسن تجريبي"),
            FullName.Create("Elderly Test"),
            Gender.Male,
            DateOnly.FromDateTime(Now.AddYears(-70)),
            DateOnly.FromDateTime(Now));

        families.Families.Add(family);
        families.Elderlies.Add(elderly);
        await families.SaveChangesAsync();

        return (family, elderly);
    }

    private sealed class FakeCheckoutPricing : ICaregiverBookingPricing
    {
        public Task<Result<CaregiverBookingPrice>> GetBookingPriceAsync(
            CaregiverId caregiverId,
            BookingShiftType shiftType,
            TimeOnly startTime,
            TimeOnly endTime,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                Result<CaregiverBookingPrice>.Success(
                    new CaregiverBookingPrice(BookingCaregiverType.Medical, 500m)));
    }

    private static void SeedCompleted(
        FamiliesDbContext families,
        Guid? areaId,
        Guid? serviceId,
        BookingShiftType shiftType,
        decimal baseFee,
        TimeOnly start,
        TimeOnly end,
        DateTime completedOn)
    {
        DateTime createdOn = completedOn.AddHours(-1);
        Booking booking = Booking.Create(
            FamilyId.New(),
            UserId.New(),
            ElderlyId.New(),
            CaregiverId.New(),
            BookingCaregiverType.Companion,
            shiftType,
            DateOnly.FromDateTime(createdOn),
            start,
            end,
            "Heliopolis, Cairo",
            null,
            BookingPriceSnapshot.Calculate(baseFee, 10m, 5m, 1),
            createdOn.AddHours(24),
            DateOnly.FromDateTime(createdOn),
            createdOn,
            areaId,
            serviceId);

        booking.MarkAsPaid($"order-{Guid.NewGuid():N}", $"tx-{Guid.NewGuid():N}", createdOn);
        booking.AcceptByCaregiver(createdOn);
        booking.StartVisit(createdOn);
        booking.CompleteVisit(null, completedOn);
        families.Bookings.Add(booking);
    }

    private static void SeedUnpaid(
        FamiliesDbContext families,
        Guid areaId,
        Guid serviceId)
    {
        Booking booking = Booking.Create(
            FamilyId.New(),
            UserId.New(),
            ElderlyId.New(),
            CaregiverId.New(),
            BookingCaregiverType.Companion,
            BookingShiftType.Hourly,
            DateOnly.FromDateTime(Now),
            new TimeOnly(9, 0),
            new TimeOnly(17, 0),
            "Heliopolis, Cairo",
            null,
            BookingPriceSnapshot.Calculate(500m, 10m, 5m, 1),
            Now.AddHours(23),
            DateOnly.FromDateTime(Now),
            Now,
            areaId,
            serviceId);
        families.Bookings.Add(booking);
    }

    private static async Task<(Guid AreaId, Guid ServiceId)> SeedCatalogAsync(
        CaregiversDbContext caregivers)
    {
        var governorate = Governorate.Create($"Gov {Guid.NewGuid():N}", $"Gov {Guid.NewGuid():N}");
        caregivers.Governorates.Add(governorate);
        var city = City.Create(governorate.Id, $"City {Guid.NewGuid():N}", $"City {Guid.NewGuid():N}");
        caregivers.Cities.Add(city);
        var area = Area.Create(city.Id, $"Area {Guid.NewGuid():N}", $"Area {Guid.NewGuid():N}");
        caregivers.Areas.Add(area);
        var service = Service.Create(
            $"Svc {Guid.NewGuid():N}", $"Svc {Guid.NewGuid():N}",
            "icon.png", Sanad.Modules.Caregivers.Domain.Caregivers.CaregiverType.Companion, true);
        caregivers.Services.Add(service);
        await caregivers.SaveChangesAsync();
        return (area.Id.Value, service.Id.Value);
    }

    private sealed class FixedClock(DateTime now) : IDateTimeProvider
    {
        public DateTime UtcNow => now;
    }
}
