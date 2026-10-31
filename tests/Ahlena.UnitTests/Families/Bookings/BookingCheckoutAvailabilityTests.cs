using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Enums;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.BuildingBlocks.Domain.ValueObjects;
using Ahlena.Modules.Families.Application.Abstractions.Caregivers;
using Ahlena.Modules.Families.Application.Abstractions.Data;
using Ahlena.Modules.Families.Application.Bookings;
using Ahlena.Modules.Families.Domain.Bookings;
using Ahlena.Modules.Families.Domain.Elderlies;
using Ahlena.Modules.Families.Domain.Families;
using Ahlena.Modules.Families.Infrastructure.Persistence;
using Ahlena.UnitTests.Finance;
using Ahlena.UnitTests.Support;

namespace Ahlena.UnitTests.Families.Bookings;

/// <summary>
/// The additive availability gate at checkout: the existing overlap predicate
/// keeps its priority and error code, and a slot outside the caregiver's
/// declared date-specific availability is refused with
/// <c>Bookings.AvailabilityConflict</c> without writing a booking.
/// </summary>
public sealed class BookingCheckoutAvailabilityTests
{
    private static readonly DateTime UtcNow = new(2026, 10, 25, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly BookingDate = new(2026, 10, 27);

    private sealed class FakePricing : ICaregiverBookingPricing
    {
        public Task<Result<CaregiverBookingPrice>> GetBookingPriceAsync(
            CaregiverId caregiverId,
            BookingShiftType shiftType,
            TimeOnly startTime,
            TimeOnly endTime,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<CaregiverBookingPrice>.Success(
                new CaregiverBookingPrice(BookingCaregiverType.Medical, 500m)));
    }

    private sealed class NotFoundPricing : ICaregiverBookingPricing
    {
        public Task<Result<CaregiverBookingPrice>> GetBookingPriceAsync(
            CaregiverId caregiverId,
            BookingShiftType shiftType,
            TimeOnly startTime,
            TimeOnly endTime,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<CaregiverBookingPrice>.Failure(
                new Error("Caregivers.NotFound", "Caregiver not found.")));
    }

    private static FamiliesDbContext CreateInMemoryDb() =>
        new(new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static (FamiliesDbContext Db, Family Family, Elderly Elderly) SeedInMemory()
    {
        var db = CreateInMemoryDb();
        var family = Family.Create(UserId.New(), "Availability Family");
        var elderly = Elderly.Create(
            family.OwnerUserId,
            UserId.New(),
            family.Id,
            FamilyRelationshipType.Father,
            FullName.Create("مسن"),
            FullName.Create("Elderly"),
            Gender.Male,
            new DateOnly(1950, 1, 1),
            DateOnly.FromDateTime(UtcNow));

        db.Families.Add(family);
        db.Elderlies.Add(elderly);
        db.SaveChanges();

        return (db, family, elderly);
    }

    private static CreateBookingCheckoutCommandHandler Handler(
        IFamiliesDbContext db,
        ICaregiverBookingPricing pricing,
        ICaregiverAvailabilityRules availability) =>
        new(
            db,
            pricing,
            new FixedPlatformChargeRules(15m, 0m),
            FixedPlatformControls.Operational(),
            new StubIdempotentResponseSnapshotter(),
            availability,
            new PassThroughCaregiverBookingCheckoutGuard());

    private static CreateBookingCheckoutCommand Command(Family family, Elderly elderly) =>
        new(
            family.OwnerUserId,
            elderly.Id,
            new CaregiverId(Guid.Parse("11111111-2222-3333-4444-555555555555")),
            BookingShiftType.HomeVisit,
            BookingDate,
            new TimeOnly(10, 0),
            new TimeOnly(12, 0),
            "12 Nile St, Cairo",
            null,
            DateOnly.FromDateTime(UtcNow),
            UtcNow);

    [Fact]
    public async Task OutsideDeclaredAvailability_IsAnAvailabilityConflictWithNoBooking()
    {
        var (db, family, elderly) = SeedInMemory();

        var result = await Handler(
                db,
                new FakePricing(),
                new StubCaregiverAvailabilityRules(isAvailable: false))
            .Handle(Command(family, elderly), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Bookings.AvailabilityConflict", result.Error.Code);
        Assert.Empty(db.Bookings);
    }

    [Fact]
    public async Task InsideDeclaredAvailability_CreatesTheBooking()
    {
        var (db, family, elderly) = SeedInMemory();

        var result = await Handler(
                db,
                new FakePricing(),
                new StubCaregiverAvailabilityRules(isAvailable: true))
            .Handle(Command(family, elderly), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsReplay);
        Assert.Single(db.Bookings);
    }

    [Fact]
    public async Task ExistingOverlap_StaysScheduleConflictAndIsCheckedBeforeAvailability()
    {
        var (db, family, elderly) = SeedInMemory();
        var command = Command(family, elderly);

        var first = await Handler(
                db,
                new FakePricing(),
                new StubCaregiverAvailabilityRules(isAvailable: true))
            .Handle(command, CancellationToken.None);
        Assert.True(first.IsSuccess);

        // The slot is now taken. Even though the availability gate would also
        // refuse it, the existing overlap rule keeps its precedence and code.
        var second = await Handler(
                db,
                new FakePricing(),
                new StubCaregiverAvailabilityRules(isAvailable: false))
            .Handle(command, CancellationToken.None);

        Assert.True(second.IsFailure);
        Assert.Equal("Bookings.ScheduleConflict", second.Error.Code);
        Assert.Single(db.Bookings);
    }

    [Fact]
    public async Task PricingFailure_IsSurfacedBeforeAvailabilityIsConsulted()
    {
        var (db, family, elderly) = SeedInMemory();
        var availability = new TrackingAvailabilityRules(isAvailable: false);

        var result = await Handler(db, new NotFoundPricing(), availability)
            .Handle(Command(family, elderly), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Caregivers.NotFound", result.Error.Code);
        Assert.Equal(0, availability.Calls);
        Assert.Empty(db.Bookings);
    }

    private sealed class TrackingAvailabilityRules(bool isAvailable) : ICaregiverAvailabilityRules
    {
        public int Calls { get; private set; }

        public Task<bool> IsAvailableAsync(
            CaregiverId caregiverId,
            DateOnly date,
            BookingShiftType shiftType,
            TimeOnly startTime,
            TimeOnly endTime,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(isAvailable);
        }
    }
}
