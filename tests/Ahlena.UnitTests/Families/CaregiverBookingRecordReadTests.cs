using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Domain.Enums;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.BuildingBlocks.Domain.ValueObjects;
using Ahlena.Modules.Families.Application.Bookings;
using Ahlena.Modules.Families.Domain.Bookings;
using Ahlena.Modules.Families.Domain.Elderlies;
using Ahlena.Modules.Families.Domain.Families;
using Ahlena.Modules.Families.Domain.Reports;
using Ahlena.Modules.Families.Infrastructure.Persistence;

namespace Ahlena.UnitTests.Families;

/// <summary>
/// M5 caregiver reads of a booking's emergency contact and its recorded visit
/// history. Both are strictly caregiver-scoped: a booking that belongs to
/// another caregiver is <c>Bookings.NotFound</c>. The emergency contact is
/// exposed only across the live visit window (<c>InProgress</c>, or
/// <c>Confirmed</c> and not yet started) and is <c>Bookings.EmergencyContactNotAllowed</c>
/// (403) otherwise; the visit history is <c>null</c> — not an empty array — when
/// nothing was recorded.
/// </summary>
public sealed class CaregiverBookingRecordReadTests
{
    [Fact]
    public async Task EmergencyContact_is_returned_while_the_visit_is_in_progress()
    {
        var (db, family, elderly) = SeedFamily();
        DateTime now = DateTime.UtcNow;
        var caregiverId = CaregiverId.New();
        Booking booking = ConfirmedBooking(family, elderly, caregiverId, now);
        booking.StartVisit(now.AddHours(1));
        db.Bookings.Add(booking);
        db.SaveChanges();

        var result = await new GetCaregiverBookingEmergencyContactQueryHandler(db).Handle(
            new GetCaregiverBookingEmergencyContactQuery(caregiverId, booking.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("Nour", result.Value!.Name);
        Assert.Equal("Daughter", result.Value.Relationship);
        Assert.Equal("+201001234567", result.Value.PhoneNumber);
    }

    [Fact]
    public async Task EmergencyContact_is_returned_for_a_confirmed_visit_not_yet_started()
    {
        var (db, family, elderly) = SeedFamily();
        DateTime now = DateTime.UtcNow;
        var caregiverId = CaregiverId.New();
        Booking booking = ConfirmedBooking(family, elderly, caregiverId, now);
        db.Bookings.Add(booking);
        db.SaveChanges();

        var result = await new GetCaregiverBookingEmergencyContactQueryHandler(db).Handle(
            new GetCaregiverBookingEmergencyContactQuery(caregiverId, booking.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("+201001234567", result.Value!.PhoneNumber);
    }

    [Fact]
    public async Task EmergencyContact_is_denied_when_the_visit_already_started_and_was_completed()
    {
        var (db, family, elderly) = SeedFamily();
        DateTime now = DateTime.UtcNow;
        var caregiverId = CaregiverId.New();
        Booking booking = ConfirmedBooking(family, elderly, caregiverId, now);
        booking.StartVisit(now.AddHours(1));
        booking.CompleteVisit("done", now.AddHours(2));
        db.Bookings.Add(booking);
        db.SaveChanges();

        var result = await new GetCaregiverBookingEmergencyContactQueryHandler(db).Handle(
            new GetCaregiverBookingEmergencyContactQuery(caregiverId, booking.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Bookings.EmergencyContactNotAllowed", result.Error.Code);
    }

    [Fact]
    public async Task EmergencyContact_is_denied_for_a_cancelled_booking()
    {
        var (db, family, elderly) = SeedFamily();
        DateTime now = DateTime.UtcNow;
        var caregiverId = CaregiverId.New();
        Booking booking = ConfirmedBooking(family, elderly, caregiverId, now);
        booking.CancelByFamily("change of plans", now.AddHours(1));
        db.Bookings.Add(booking);
        db.SaveChanges();

        var result = await new GetCaregiverBookingEmergencyContactQueryHandler(db).Handle(
            new GetCaregiverBookingEmergencyContactQuery(caregiverId, booking.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Bookings.EmergencyContactNotAllowed", result.Error.Code);
    }

    [Fact]
    public async Task EmergencyContact_is_not_found_for_another_caregivers_booking()
    {
        var (db, family, elderly) = SeedFamily();
        DateTime now = DateTime.UtcNow;
        var owner = CaregiverId.New();
        Booking booking = ConfirmedBooking(family, elderly, owner, now);
        db.Bookings.Add(booking);
        db.SaveChanges();

        var result = await new GetCaregiverBookingEmergencyContactQueryHandler(db).Handle(
            new GetCaregiverBookingEmergencyContactQuery(CaregiverId.New(), booking.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Bookings.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task EmergencyContact_is_null_when_the_dependent_stores_none()
    {
        var (db, family, elderly) = SeedFamily(withEmergencyContact: false);
        DateTime now = DateTime.UtcNow;
        var caregiverId = CaregiverId.New();
        Booking booking = ConfirmedBooking(family, elderly, caregiverId, now);
        db.Bookings.Add(booking);
        db.SaveChanges();

        var result = await new GetCaregiverBookingEmergencyContactQueryHandler(db).Handle(
            new GetCaregiverBookingEmergencyContactQuery(caregiverId, booking.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task VisitHistory_reports_the_recorded_reports_and_no_raw_photo_key()
    {
        var (db, family, elderly) = SeedFamily();
        DateTime now = DateTime.UtcNow;
        var caregiverId = CaregiverId.New();
        UserId caregiverUserId = UserId.New();
        Booking booking = ConfirmedBooking(family, elderly, caregiverId, now);
        db.Bookings.Add(booking);

        db.MedicalReports.Add(MedicalReport.Create(
            booking.Id,
            family.Id,
            elderly.Id,
            caregiverId,
            caregiverUserId,
            BookingCaregiverType.Medical,
            120,
            80,
            70,
            36.5m,
            "Morning reading",
            now.AddHours(2),
            VisitReportAssessment.NeedsFollowUp,
            now.AddHours(3),
            true,
            now.AddHours(3),
            caregiverUserId,
            "private/medical/photo.jpg"));
        db.SaveChanges();

        var result = await new GetCaregiverBookingVisitHistoryQueryHandler(db).Handle(
            new GetCaregiverBookingVisitHistoryQuery(caregiverId, booking.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        CaregiverBookingVisitHistoryItem item = Assert.Single(result.Value!);
        Assert.Equal("medical", item.ReportType);
        Assert.True(item.PhotoAvailable);

        // The response is a shape with no key field at all: the private photo
        // key can never be serialized.
        Assert.DoesNotContain(
            typeof(CaregiverBookingVisitHistoryItem).GetProperties(),
            property => property.Name.Contains("PhotoKey", StringComparison.OrdinalIgnoreCase) ||
                        property.Name.Contains("Key", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task VisitHistory_is_not_found_for_another_caregivers_booking()
    {
        var (db, family, elderly) = SeedFamily();
        DateTime now = DateTime.UtcNow;
        var owner = CaregiverId.New();
        Booking booking = ConfirmedBooking(family, elderly, owner, now);
        db.Bookings.Add(booking);
        db.SaveChanges();

        var result = await new GetCaregiverBookingVisitHistoryQueryHandler(db).Handle(
            new GetCaregiverBookingVisitHistoryQuery(CaregiverId.New(), booking.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Bookings.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task VisitHistory_is_null_when_no_report_was_recorded()
    {
        var (db, family, elderly) = SeedFamily();
        DateTime now = DateTime.UtcNow;
        var caregiverId = CaregiverId.New();
        Booking booking = ConfirmedBooking(family, elderly, caregiverId, now);
        db.Bookings.Add(booking);
        db.SaveChanges();

        var result = await new GetCaregiverBookingVisitHistoryQueryHandler(db).Handle(
            new GetCaregiverBookingVisitHistoryQuery(caregiverId, booking.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    private static FamiliesDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static (FamiliesDbContext DbContext, Family Family, Elderly Elderly) SeedFamily(
        bool withEmergencyContact = true)
    {
        var db = CreateDbContext();
        var family = Family.Create(UserId.New(), "Caregiver Record Family");

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

        if (withEmergencyContact)
        {
            elderly.SetEmergencyContact("Nour", "Daughter", "+201001234567");
        }

        db.Families.Add(family);
        db.Elderlies.Add(elderly);
        db.SaveChanges();

        return (db, family, elderly);
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
