using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Caregivers.Application.Availability;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Caregivers.Infrastructure.Persistence;

namespace Sanad.UnitTests.Caregivers;

/// <summary>
/// The single date-specific rule set behind both the advisory
/// <c>availability/day</c> read and the checkout containment check. These facts
/// pin the declared-window mapping, busy subtraction, the mirrored
/// cross-midnight gap of the existing checkout predicate, and the documented
/// DST behaviour.
/// </summary>
public sealed class CaregiverDayAvailabilityTests
{
    // 2026-08-22 is a Saturday; no DST transition happens on that day.
    private static readonly DateOnly Saturday = new(2026, 8, 22);

    // 2026-08-25 is a Tuesday with no declared window.
    private static readonly DateOnly Tuesday = new(2026, 8, 25);

    // Egypt springs forward at 00:00 on 2026-04-24 (a Friday).
    private static readonly DateOnly SpringForwardFriday = new(2026, 4, 24);

    // Egypt falls back at 24:00 on 2026-10-29 (a Thursday): 23:00 repeats.
    private static readonly DateOnly FallBackThursday = new(2026, 10, 29);

    [Theory]
    [InlineData(MedicalShiftType.EightHourMorning, BookingShiftType.EightHourShift, 8, 16, false)]
    [InlineData(MedicalShiftType.EightHourNight, BookingShiftType.EightHourShift, 0, 8, false)]
    [InlineData(MedicalShiftType.TwelveHourNight, BookingShiftType.TwelveHourShift, 20, 8, true)]
    [InlineData(MedicalShiftType.TwentyFourHourLiveIn, BookingShiftType.TwentyFourHourShift, 8, 8, true)]
    public void MedicalShift_MapsByStoredDuration(
        MedicalShiftType storedShift,
        BookingShiftType requestedShift,
        int startHour,
        int endHour,
        bool endsNextDay)
    {
        Caregiver caregiver = ActiveMedical(
            caregiver => caregiver.AddMedicalShift(DayOfWeek.Saturday, storedShift));

        IReadOnlyList<CaregiverDayAvailabilityInterval> intervals =
            CaregiverDayAvailabilityRules.Build(caregiver, Saturday, requestedShift, []);

        AssertSingleInterval(
            intervals,
            Saturday,
            new TimeOnly(startHour, 0),
            new TimeOnly(endHour, 0),
            endsNextDay);
    }

    [Fact]
    public void MedicalHomeVisit_MapsOnlyHomeVisitWindows()
    {
        Caregiver caregiver = ActiveMedical(
            caregiver => caregiver.AddMedicalHomeVisitWindow(
                DayOfWeek.Saturday,
                new TimeOnly(9, 0),
                new TimeOnly(12, 0)));

        AssertSingleInterval(
            CaregiverDayAvailabilityRules.Build(
                caregiver,
                Saturday,
                BookingShiftType.HomeVisit,
                []),
            Saturday,
            new TimeOnly(9, 0),
            new TimeOnly(12, 0));

        // The same day asked as an eight-hour shift has no declared shift.
        Assert.Empty(CaregiverDayAvailabilityRules.Build(
            caregiver,
            Saturday,
            BookingShiftType.EightHourShift,
            []));
    }

    [Theory]
    [InlineData(CompanionBookingType.EightHourDay, BookingShiftType.EightHourShift, 8, 16, false)]
    [InlineData(CompanionBookingType.Overnight, BookingShiftType.TwelveHourShift, 20, 8, true)]
    [InlineData(CompanionBookingType.Hourly, BookingShiftType.Hourly, 9, 13, false)]
    public void CompanionWindow_MapsByProduct(
        CompanionBookingType storedProduct,
        BookingShiftType requestedShift,
        int startHour,
        int endHour,
        bool endsNextDay)
    {
        Caregiver caregiver = ActiveCompanion(
            caregiver => caregiver.AddCompanionAvailabilityWindow(
                storedProduct,
                DayOfWeek.Saturday,
                new TimeOnly(startHour, 0),
                new TimeOnly(endHour, 0)));

        IReadOnlyList<CaregiverDayAvailabilityInterval> intervals =
            CaregiverDayAvailabilityRules.Build(caregiver, Saturday, requestedShift, []);

        AssertSingleInterval(
            intervals,
            Saturday,
            new TimeOnly(startHour, 0),
            new TimeOnly(endHour, 0),
            endsNextDay);
    }

    [Fact]
    public void DayWithoutADeclaredWindow_ReturnsNoIntervals()
    {
        Caregiver caregiver = ActiveMedical(
            caregiver => caregiver.AddMedicalShift(
                DayOfWeek.Saturday,
                MedicalShiftType.EightHourMorning));

        Assert.Empty(CaregiverDayAvailabilityRules.Build(
            caregiver,
            Tuesday,
            BookingShiftType.EightHourShift,
            []));
    }

    [Fact]
    public void ManuallyUnavailableCaregiver_ReturnsNoIntervals()
    {
        Caregiver caregiver = ActiveMedical(
            caregiver => caregiver.AddMedicalShift(
                DayOfWeek.Saturday,
                MedicalShiftType.EightHourMorning));
        caregiver.BecomeUnavailable();

        Assert.Empty(CaregiverDayAvailabilityRules.Build(
            caregiver,
            Saturday,
            BookingShiftType.EightHourShift,
            []));
    }

    [Fact]
    public void NonActiveCaregiver_ReturnsNoIntervals()
    {
        Caregiver caregiver = Caregiver.Create(UserId.New(), CaregiverType.Medical);
        caregiver.AddMedicalShift(DayOfWeek.Saturday, MedicalShiftType.EightHourMorning);

        Assert.Equal(CaregiverStatus.Onboarding, caregiver.Status);
        Assert.Empty(CaregiverDayAvailabilityRules.Build(
            caregiver,
            Saturday,
            BookingShiftType.EightHourShift,
            []));
    }

    [Fact]
    public void HourlyWindow_SubtractsOnlyTheBookedPortion()
    {
        Caregiver caregiver = ActiveCompanion(
            caregiver => caregiver.AddCompanionAvailabilityWindow(
                CompanionBookingType.Hourly,
                DayOfWeek.Saturday,
                new TimeOnly(9, 0),
                new TimeOnly(13, 0)));

        IReadOnlyList<CaregiverDayAvailabilityInterval> intervals =
            CaregiverDayAvailabilityRules.Build(
                caregiver,
                Saturday,
                BookingShiftType.Hourly,
                [new CaregiverBusyWindow(new TimeOnly(10, 0), new TimeOnly(11, 0))]);

        Assert.Collection(
            intervals,
            first => AssertInterval(
                first,
                Saturday,
                new TimeOnly(9, 0),
                Saturday,
                new TimeOnly(10, 0)),
            second => AssertInterval(
                second,
                Saturday,
                new TimeOnly(11, 0),
                Saturday,
                new TimeOnly(13, 0)));
    }

    [Fact]
    public void FixedShift_IsDroppedEntirelyWhenABusyWindowOverlaps()
    {
        Caregiver caregiver = ActiveMedical(
            caregiver => caregiver.AddMedicalShift(
                DayOfWeek.Saturday,
                MedicalShiftType.EightHourMorning));

        IReadOnlyList<CaregiverDayAvailabilityInterval> intervals =
            CaregiverDayAvailabilityRules.Build(
                caregiver,
                Saturday,
                BookingShiftType.EightHourShift,
                [new CaregiverBusyWindow(new TimeOnly(10, 0), new TimeOnly(11, 0))]);

        Assert.Empty(intervals);
    }

    /// <summary>
    /// The checkout predicate compares raw day times, so a booking that falls
    /// inside a window which crosses midnight is invisible to it. The read
    /// mirrors that existing gap instead of silently diverging from checkout.
    /// </summary>
    [Fact]
    public void CrossMidnightBusyWindow_IsMirroredNotSubtracted()
    {
        Caregiver caregiver = ActiveMedical(
            caregiver => caregiver.AddMedicalShift(
                DayOfWeek.Saturday,
                MedicalShiftType.TwelveHourNight));

        IReadOnlyList<CaregiverDayAvailabilityInterval> intervals =
            CaregiverDayAvailabilityRules.Build(
                caregiver,
                Saturday,
                BookingShiftType.TwelveHourShift,
                [new CaregiverBusyWindow(new TimeOnly(22, 0), new TimeOnly(23, 0))]);

        AssertSingleInterval(
            intervals,
            Saturday,
            new TimeOnly(20, 0),
            new TimeOnly(8, 0),
            endsNextDay: true);
    }

    [Fact]
    public void SpringForwardGap_OmitsTheIntervalWhoseBoundaryDoesNotExist()
    {
        Caregiver caregiver = ActiveMedical(
            caregiver => caregiver.AddMedicalShift(
                DayOfWeek.Friday,
                MedicalShiftType.EightHourNight));

        DateTime midnight = SpringForwardFriday.ToDateTime(TimeOnly.MinValue);
        Assert.True(CaregiverAvailabilityTimeZone.Zone.IsInvalidTime(midnight));

        Assert.Empty(CaregiverDayAvailabilityRules.Build(
            caregiver,
            SpringForwardFriday,
            BookingShiftType.EightHourShift,
            []));
    }

    [Fact]
    public void FallBackAmbiguousBoundary_ResolvesToTheStandardOffset()
    {
        Caregiver caregiver = ActiveCompanion(
            caregiver => caregiver.AddCompanionAvailabilityWindow(
                CompanionBookingType.Hourly,
                DayOfWeek.Thursday,
                new TimeOnly(22, 0),
                new TimeOnly(23, 0)));

        TimeZoneInfo zone = CaregiverAvailabilityTimeZone.Zone;
        DateTime localEnd = FallBackThursday.ToDateTime(new TimeOnly(23, 0));
        Assert.True(zone.IsAmbiguousTime(localEnd));

        TimeSpan standard = zone.BaseUtcOffset;
        TimeSpan daylight = zone.GetUtcOffset(
            FallBackThursday.ToDateTime(new TimeOnly(12, 0)));
        Assert.True(daylight > standard);

        CaregiverDayAvailabilityInterval interval = Assert.Single(
            CaregiverDayAvailabilityRules.Build(
                caregiver,
                FallBackThursday,
                BookingShiftType.Hourly,
                []));

        Assert.Equal(
            new DateTimeOffset(
                FallBackThursday.ToDateTime(new TimeOnly(22, 0)),
                daylight).ToUniversalTime(),
            interval.StartsAt);
        Assert.Equal(
            new DateTimeOffset(localEnd, standard).ToUniversalTime(),
            interval.EndsAt);
    }

    [Fact]
    public void Contains_UsesTheSameRuleAsTheRead()
    {
        Caregiver caregiver = ActiveCompanion(
            caregiver => caregiver.AddCompanionAvailabilityWindow(
                CompanionBookingType.Hourly,
                DayOfWeek.Saturday,
                new TimeOnly(9, 0),
                new TimeOnly(13, 0)));

        CaregiverBusyWindow[] busy =
            [new CaregiverBusyWindow(new TimeOnly(10, 0), new TimeOnly(11, 0))];

        Assert.True(IsContained(new TimeOnly(9, 0), new TimeOnly(10, 0)));
        Assert.True(IsContained(new TimeOnly(11, 0), new TimeOnly(13, 0)));
        Assert.False(IsContained(new TimeOnly(8, 0), new TimeOnly(9, 0)));
        Assert.False(IsContained(new TimeOnly(10, 0), new TimeOnly(11, 0)));
        // Spans two returned intervals, so it is not contained in one.
        Assert.False(IsContained(new TimeOnly(9, 0), new TimeOnly(13, 0)));

        bool IsContained(TimeOnly start, TimeOnly end) =>
            CaregiverDayAvailabilityRules.Contains(
                caregiver,
                Saturday,
                BookingShiftType.Hourly,
                busy,
                start,
                end);
    }

    [Fact]
    public async Task Handler_ReturnsTheDocumentedZoneAndUtcIntervals()
    {
        await using CaregiversDbContext db = CreateDbContext();

        Caregiver caregiver = ActiveCompanion(
            caregiver => caregiver.AddCompanionAvailabilityWindow(
                CompanionBookingType.Hourly,
                DayOfWeek.Saturday,
                new TimeOnly(9, 0),
                new TimeOnly(13, 0)));

        db.Caregivers.Add(caregiver);
        await db.SaveChangesAsync();

        var result = await Handler(db, []).Handle(
            new GetCaregiverDayAvailabilityQuery(
                caregiver.Id.Value,
                Saturday,
                BookingShiftType.Hourly),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(caregiver.Id.Value, result.Value.CaregiverId);
        Assert.Equal(Saturday, result.Value.Date);
        Assert.Equal("Africa/Cairo", result.Value.TimeZoneId);
        Assert.Equal(BookingShiftType.Hourly, result.Value.ShiftType);
        AssertSingleInterval(
            result.Value.Intervals,
            Saturday,
            new TimeOnly(9, 0),
            new TimeOnly(13, 0));
    }

    [Fact]
    public async Task Handler_RejectsAnEmptyCaregiverIdAndMissingDate()
    {
        await using CaregiversDbContext db = CreateDbContext();
        var handler = Handler(db, []);

        var emptyId = await handler.Handle(
            new GetCaregiverDayAvailabilityQuery(
                Guid.Empty,
                Saturday,
                BookingShiftType.Hourly),
            CancellationToken.None);

        var missingDate = await handler.Handle(
            new GetCaregiverDayAvailabilityQuery(
                Guid.NewGuid(),
                default,
                BookingShiftType.Hourly),
            CancellationToken.None);

        Assert.True(emptyId.IsFailure);
        Assert.Equal("Caregivers.Availability.InvalidQuery", emptyId.Error.Code);
        Assert.True(missingDate.IsFailure);
        Assert.Equal("Caregivers.Availability.InvalidQuery", missingDate.Error.Code);
    }

    [Fact]
    public async Task Handler_ReturnsNotFoundForAnUnknownCaregiver()
    {
        await using CaregiversDbContext db = CreateDbContext();

        var result = await Handler(db, []).Handle(
            new GetCaregiverDayAvailabilityQuery(
                Guid.NewGuid(),
                Saturday,
                BookingShiftType.Hourly),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Caregivers.NotFound", result.Error.Code);
    }

    private static GetCaregiverDayAvailabilityQueryHandler Handler(
        ICaregiversDbContext db,
        IReadOnlyList<CaregiverBusyWindow> busy) =>
        new(db, new StubBusyBookingLookup(busy));

    private static Caregiver ActiveMedical(Action<Caregiver> configure)
    {
        Caregiver caregiver = Caregiver.Create(UserId.New(), CaregiverType.Medical);
        configure(caregiver);
        caregiver.TransitionToActive();
        caregiver.BecomeAvailable(CaregiverTestData.CurrentDate);
        return caregiver;
    }

    private static Caregiver ActiveCompanion(Action<Caregiver> configure)
    {
        Caregiver caregiver = Caregiver.Create(UserId.New(), CaregiverType.Companion);
        configure(caregiver);
        caregiver.TransitionToActive();
        caregiver.BecomeAvailable(CaregiverTestData.CurrentDate);
        return caregiver;
    }

    private static CaregiversDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<CaregiversDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static void AssertSingleInterval(
        IReadOnlyList<CaregiverDayAvailabilityInterval> intervals,
        DateOnly date,
        TimeOnly start,
        TimeOnly end,
        bool endsNextDay = false)
    {
        CaregiverDayAvailabilityInterval interval = Assert.Single(intervals);

        AssertInterval(
            interval,
            date,
            start,
            endsNextDay ? date.AddDays(1) : date,
            end);
    }

    private static void AssertInterval(
        CaregiverDayAvailabilityInterval interval,
        DateOnly startDate,
        TimeOnly start,
        DateOnly endDate,
        TimeOnly end)
    {
        Assert.Equal(Cairo(startDate, start), interval.StartsAt);
        Assert.Equal(Cairo(endDate, end), interval.EndsAt);
        Assert.Equal(TimeSpan.Zero, interval.StartsAt.Offset);
        Assert.Equal(TimeSpan.Zero, interval.EndsAt.Offset);
        Assert.True(interval.EndsAt > interval.StartsAt);
    }

    private static DateTimeOffset Cairo(DateOnly date, TimeOnly time)
    {
        DateTime local = date.ToDateTime(time);
        TimeZoneInfo zone = CaregiverAvailabilityTimeZone.Zone;
        Assert.False(zone.IsInvalidTime(local));

        TimeSpan offset = zone.IsAmbiguousTime(local)
            ? zone.BaseUtcOffset
            : zone.GetUtcOffset(local);

        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    private sealed class StubBusyBookingLookup(
        IReadOnlyList<CaregiverBusyWindow> busy) : ICaregiverBusyBookingLookup
    {
        public Task<IReadOnlyList<CaregiverBusyWindow>> GetBusyWindowsAsync(
            CaregiverId caregiverId,
            DateOnly date,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(busy);
    }
}
