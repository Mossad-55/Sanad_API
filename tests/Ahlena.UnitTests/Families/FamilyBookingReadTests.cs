using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Domain.Enums;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.BuildingBlocks.Domain.ValueObjects;
using Ahlena.Modules.Families.Application.Bookings;
using Ahlena.Modules.Families.Domain.Bookings;
using Ahlena.Modules.Families.Domain.Elderlies;
using Ahlena.Modules.Families.Domain.Families;
using Ahlena.Modules.Families.Infrastructure.Persistence;
using Xunit;

namespace Ahlena.UnitTests.Families;

/// <summary>
/// Family booking read completeness (mobile item 5). The family booking detail
/// must publish the real stored dependent facts the app needs — the photo flag
/// and the stored emergency contact — with explicit nullability, and it must
/// degrade to honest nulls when the dependent row is gone. The caregiver-only
/// record members stay null on the family read: they are read from the
/// caregiver's own records, not from the family's.
/// </summary>
public sealed class FamilyBookingReadTests
{
    [Fact]
    public async Task Family_booking_detail_publishes_the_elderly_photo_flag_and_the_stored_emergency_contact()
    {
        var (dbContext, family, elderly) = SeedFamily();
        DateTime now = DateTime.UtcNow;
        var caregiverId = CaregiverId.New();

        elderly.ChangePhoto("private/dependents/photo.jpg");
        elderly.SetEmergencyContact("Nour", "Daughter", "+201001234567");

        Booking booking = ConfirmedBooking(family, elderly, caregiverId, now);
        dbContext.Bookings.Add(booking);
        dbContext.SaveChanges();

        var result = await new GetFamilyBookingDetailQueryHandler(dbContext)
            .Handle(
                new GetFamilyBookingDetailQuery(family.OwnerUserId, booking.Id),
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        BookingDetailResponse response = result.Value;

        // The stored dependent facts, read from the dependent row itself.
        Assert.True(response.Elderly.HasPhoto);
        Assert.NotNull(response.Elderly.EmergencyContact);
        Assert.Equal("Nour", response.Elderly.EmergencyContact!.Name);
        Assert.Equal("Daughter", response.Elderly.EmergencyContact.Relationship);
        Assert.Equal("+201001234567", response.Elderly.EmergencyContact.PhoneNumber);

        // The pre-existing summary keeps its meaning.
        Assert.Equal(elderly.Id.Value, response.Elderly.ElderlyId);
        Assert.Equal(elderly.ArabicFullName.Value, response.Elderly.ArabicFullName);
        Assert.Equal(elderly.DateOfBirth, response.Elderly.DateOfBirth);
        Assert.Equal(elderly.Gender, response.Elderly.Gender);
    }

    [Fact]
    public async Task Family_booking_detail_keeps_the_emergency_contact_null_when_none_is_stored()
    {
        var (dbContext, family, elderly) = SeedFamily();
        DateTime now = DateTime.UtcNow;
        var caregiverId = CaregiverId.New();

        Booking booking = ConfirmedBooking(family, elderly, caregiverId, now);
        dbContext.Bookings.Add(booking);
        dbContext.SaveChanges();

        var result = await new GetFamilyBookingDetailQueryHandler(dbContext)
            .Handle(
                new GetFamilyBookingDetailQuery(family.OwnerUserId, booking.Id),
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Elderly.EmergencyContact);
        Assert.False(result.Value.Elderly.HasPhoto);
    }

    [Fact]
    public async Task Family_booking_detail_degrades_without_throw_when_the_dependent_row_is_missing()
    {
        var (dbContext, family, _) = SeedFamily();
        DateTime now = DateTime.UtcNow;
        var caregiverId = CaregiverId.New();

        // A booking whose dependent row is gone: the read must answer with
        // honest nulls instead of throwing or fabricating a dependent.
        Booking booking = Booking.Create(
            family.Id,
            family.OwnerUserId,
            ElderlyId.New(),
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

        dbContext.Bookings.Add(booking);
        dbContext.SaveChanges();

        var result = await new GetFamilyBookingDetailQueryHandler(dbContext)
            .Handle(
                new GetFamilyBookingDetailQuery(family.OwnerUserId, booking.Id),
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(booking.ElderlyId.Value, result.Value.Elderly.ElderlyId);
        Assert.Null(result.Value.Elderly.ArabicFullName);
        Assert.Null(result.Value.Elderly.EnglishFullName);
        Assert.Null(result.Value.Elderly.Gender);
        Assert.Null(result.Value.Elderly.DateOfBirth);
        Assert.Null(result.Value.Elderly.HealthNotes);
        Assert.Null(result.Value.Elderly.EmergencyContact);
        Assert.False(result.Value.Elderly.HasPhoto);
    }

    [Fact]
    public async Task Family_booking_detail_leaves_the_caregiver_only_record_members_null()
    {
        var (dbContext, family, elderly) = SeedFamily();
        DateTime now = DateTime.UtcNow;
        var caregiverId = CaregiverId.New();

        Booking booking = ConfirmedBooking(family, elderly, caregiverId, now);
        dbContext.Bookings.Add(booking);
        dbContext.SaveChanges();

        var result = await new GetFamilyBookingDetailQueryHandler(dbContext)
            .Handle(
                new GetFamilyBookingDetailQuery(family.OwnerUserId, booking.Id),
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.VisitHistory);
        Assert.Null(result.Value.UpcomingVisits);
        Assert.Null(result.Value.FamilyReview);
    }

    private static (FamiliesDbContext DbContext, Family Family, Elderly Elderly) SeedFamily()
    {
        var dbContext = new FamiliesDbContext(
            new DbContextOptionsBuilder<FamiliesDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);

        var family = Family.Create(UserId.New(), "Booking Read Family");

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
}
