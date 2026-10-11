using Microsoft.EntityFrameworkCore;
using Sanad.API.ChatIntegration;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.CareHomes.Domain.Bookings;
using Sanad.Modules.CareHomes.Domain.Facilities;
using Sanad.Modules.CareHomes.Infrastructure.Persistence;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Caregivers.Infrastructure.Persistence;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Infrastructure.Persistence;

namespace Sanad.UnitTests.Chat;

/// <summary>
/// Opus delta — booking-open resolution behind
/// <see cref="ApiChatBookingGateway"/>: a booking-only open resolves the
/// counterpart from real Families/CareHomes rows, rejects unknown, foreign,
/// and cancelled/expired/refunded bookings, and never reveals which check
/// failed (every denial is the same null).
/// </summary>
public sealed class ChatBookingGatewayTests
{
    [Fact]
    public async Task FamilyMemberAndOwnerOpeners_ResolveTheBookedCaregiver()
    {
        await using FamiliesDbContext families = CreateFamiliesDb();
        await using CaregiversDbContext caregivers = CreateCaregiversDb();
        await using CareHomesDbContext careHomes = CreateCareHomesDb();
        var gateway = new ApiChatBookingGateway(families, caregivers, careHomes);

        UserId owner = UserId.New();
        UserId member = UserId.New();
        UserId caregiverUser = UserId.New();
        Booking booking = await SeedFamilyBookingAsync(
            families, caregivers, owner, member, caregiverUser);

        Assert.Equal(
            caregiverUser,
            await gateway.ResolveCounterpartAsync(
                booking.Id, member, default));
        Assert.Equal(
            caregiverUser,
            await gateway.ResolveCounterpartAsync(
                booking.Id, owner, default));
    }

    [Fact]
    public async Task CaregiverOpener_ResolvesTheBookingCreator()
    {
        await using FamiliesDbContext families = CreateFamiliesDb();
        await using CaregiversDbContext caregivers = CreateCaregiversDb();
        await using CareHomesDbContext careHomes = CreateCareHomesDb();
        var gateway = new ApiChatBookingGateway(families, caregivers, careHomes);

        UserId owner = UserId.New();
        UserId caregiverUser = UserId.New();
        Booking booking = await SeedFamilyBookingAsync(
            families, caregivers, owner, UserId.New(), caregiverUser);

        Assert.Equal(
            owner,
            await gateway.ResolveCounterpartAsync(
                booking.Id, caregiverUser, default));
    }

    [Fact]
    public async Task ForeignOpenerAndUnknownBooking_ResolveNothing()
    {
        await using FamiliesDbContext families = CreateFamiliesDb();
        await using CaregiversDbContext caregivers = CreateCaregiversDb();
        await using CareHomesDbContext careHomes = CreateCareHomesDb();
        var gateway = new ApiChatBookingGateway(families, caregivers, careHomes);

        UserId owner = UserId.New();
        Booking booking = await SeedFamilyBookingAsync(
            families, caregivers, owner, UserId.New(), UserId.New());

        Assert.Null(await gateway.ResolveCounterpartAsync(
            booking.Id, UserId.New(), default));
        Assert.Null(await gateway.ResolveCounterpartAsync(
            BookingId.New(), owner, default));
    }

    [Fact]
    public async Task CancelledAndExpiredFamilyBookings_ResolveNothing()
    {
        await using FamiliesDbContext families = CreateFamiliesDb();
        await using CaregiversDbContext caregivers = CreateCaregiversDb();
        await using CareHomesDbContext careHomes = CreateCareHomesDb();
        var gateway = new ApiChatBookingGateway(families, caregivers, careHomes);
        DateTime now = DateTime.UtcNow;

        UserId owner = UserId.New();
        Booking cancelled = await SeedFamilyBookingAsync(
            families, caregivers, owner, UserId.New(), UserId.New());
        cancelled.CancelByFamily("changed plans", now);

        Booking expired = await SeedFamilyBookingAsync(
            families, caregivers, owner, UserId.New(), UserId.New());
        expired.Expire(now.AddHours(2));

        await families.SaveChangesAsync();

        Assert.Null(await gateway.ResolveCounterpartAsync(
            cancelled.Id, owner, default));
        Assert.Null(await gateway.ResolveCounterpartAsync(
            expired.Id, owner, default));
    }

    [Fact]
    public async Task DeclinedFamilyBooking_RemainsResolvable()
    {
        // The frozen delta rejects only cancelled/expired/refunded, so a
        // caregiver-declined booking still resolves its counterpart.
        await using FamiliesDbContext families = CreateFamiliesDb();
        await using CaregiversDbContext caregivers = CreateCaregiversDb();
        await using CareHomesDbContext careHomes = CreateCareHomesDb();
        var gateway = new ApiChatBookingGateway(families, caregivers, careHomes);
        DateTime now = DateTime.UtcNow;

        UserId owner = UserId.New();
        UserId caregiverUser = UserId.New();
        Booking booking = await SeedFamilyBookingAsync(
            families, caregivers, owner, UserId.New(), caregiverUser);
        booking.MarkAsPaid("order-1", "tx-1", now);
        booking.DeclineByCaregiver("fully booked", now);
        await families.SaveChangesAsync();

        Assert.Equal(
            caregiverUser,
            await gateway.ResolveCounterpartAsync(
                booking.Id, owner, default));
    }

    [Fact]
    public async Task CareHomeOpeners_ResolveEachOther()
    {
        await using FamiliesDbContext families = CreateFamiliesDb();
        await using CaregiversDbContext caregivers = CreateCaregiversDb();
        await using CareHomesDbContext careHomes = CreateCareHomesDb();
        var gateway = new ApiChatBookingGateway(families, caregivers, careHomes);
        DateTime now = DateTime.UtcNow;

        UserId owner = UserId.New();
        UserId familyUser = UserId.New();
        CareHomeBooking booking = await SeedCareHomeBookingAsync(
            careHomes, owner, familyUser, now);

        Assert.Equal(
            owner,
            await gateway.ResolveCounterpartAsync(
                new BookingId(booking.Id), familyUser, default));
        Assert.Equal(
            familyUser,
            await gateway.ResolveCounterpartAsync(
                new BookingId(booking.Id), owner, default));
        Assert.Null(await gateway.ResolveCounterpartAsync(
            new BookingId(booking.Id), UserId.New(), default));
    }

    [Fact]
    public async Task CancelledCareHomeBooking_ResolvesNothing()
    {
        await using FamiliesDbContext families = CreateFamiliesDb();
        await using CaregiversDbContext caregivers = CreateCaregiversDb();
        await using CareHomesDbContext careHomes = CreateCareHomesDb();
        var gateway = new ApiChatBookingGateway(families, caregivers, careHomes);
        DateTime now = DateTime.UtcNow;

        UserId owner = UserId.New();
        UserId familyUser = UserId.New();
        CareHomeBooking booking = await SeedCareHomeBookingAsync(
            careHomes, owner, familyUser, now);
        booking.CancelByFamily(now);
        await careHomes.SaveChangesAsync();

        Assert.Null(await gateway.ResolveCounterpartAsync(
            new BookingId(booking.Id), familyUser, default));
    }

    private static async Task<Booking> SeedFamilyBookingAsync(
        FamiliesDbContext families,
        CaregiversDbContext caregivers,
        UserId owner,
        UserId member,
        UserId caregiverUser)
    {
        DateTime now = DateTime.UtcNow;

        Family family = Family.Create(owner, "Gateway family");
        family.AddMember(FamilyMember.Create(
            member, owner, FamilyRelationshipType.Son, FamilyRole.Editor));
        families.Families.Add(family);

        Caregiver caregiver = Caregiver.Create(
            caregiverUser, CaregiverType.Companion);
        caregivers.Caregivers.Add(caregiver);

        Booking booking = Booking.Create(
            family.Id,
            owner,
            ElderlyId.New(),
            caregiver.Id,
            BookingCaregiverType.Companion,
            BookingShiftType.Hourly,
            DateOnly.FromDateTime(now),
            new TimeOnly(8),
            new TimeOnly(10),
            "12 Nile St",
            null,
            BookingPriceSnapshot.Calculate(100, 10),
            now.AddHours(1),
            DateOnly.FromDateTime(now),
            now);
        families.Bookings.Add(booking);

        await families.SaveChangesAsync();
        await caregivers.SaveChangesAsync();

        return booking;
    }

    private static async Task<CareHomeBooking> SeedCareHomeBookingAsync(
        CareHomesDbContext careHomes,
        UserId owner,
        UserId familyUser,
        DateTime now)
    {
        CareHomeFacility facility = CareHomeFacility.CreateDraft(owner, now);
        careHomes.Facilities.Add(facility);

        CareHomeBooking booking = CareHomeBooking.Create(
            facility.Id,
            familyUser,
            FamilyId.New(),
            ElderlyId.New(),
            Guid.NewGuid(),
            DateOnly.FromDateTime(now.AddDays(2)),
            100m,
            0m,
            0m,
            1,
            "مسن",
            "Elderly",
            75,
            null,
            "Contact",
            null,
            null,
            null,
            now);
        careHomes.Bookings.Add(booking);

        await careHomes.SaveChangesAsync();

        return booking;
    }

    private static FamiliesDbContext CreateFamiliesDb() =>
        new(new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static CaregiversDbContext CreateCaregiversDb() =>
        new(new DbContextOptionsBuilder<CaregiversDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static CareHomesDbContext CreateCareHomesDb() =>
        new(new DbContextOptionsBuilder<CareHomesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
