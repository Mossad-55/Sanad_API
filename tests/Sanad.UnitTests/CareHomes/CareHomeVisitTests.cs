using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;
using Sanad.API.Authorization;
using Sanad.API.Controllers;
using Sanad.API.ProblemDetail;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Application.Visits;
using Sanad.Modules.CareHomes.Domain.Bookings;
using Sanad.Modules.CareHomes.Domain.Facilities;
using Sanad.Modules.CareHomes.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Families.Domain.Families;

namespace Sanad.UnitTests.CareHomes;

public sealed class CareHomeVisitTests
{
    private static readonly DateTime Now = new(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Pending_visit_expires_at_24_hours_and_cannot_be_approved_after_expiry()
    {
        CareHomeVisit visit = MakeVisit(Now.AddDays(3));

        Assert.False(visit.Expire(Now.AddHours(23)));
        Assert.True(visit.Expire(Now.AddHours(24)));
        Assert.Equal(CareHomeVisitStatus.Expired, visit.Status);
        Assert.Throws<Sanad.BuildingBlocks.Domain.Exceptions.DomainException>(() => visit.Approve(UserId.New(), Now.AddHours(24)));
    }

    [Fact]
    public void Reschedule_keeps_original_approved_until_new_slot_is_approved_then_replaces_atomically()
    {
        CareHomeVisit original = MakeVisit(Now.AddDays(3));
        original.Approve(UserId.New(), Now.AddMinutes(1));
        CareHomeVisit replacement = CareHomeVisit.RequestReschedule(original, Now.AddDays(4), Now.AddMinutes(2));

        Assert.Equal(CareHomeVisitStatus.Approved, original.Status);
        Assert.Equal(CareHomeVisitStatus.ReschedulePending, replacement.Status);

        replacement.Approve(UserId.New(), Now.AddMinutes(3));
        original.MarkRescheduled(UserId.New(), Now.AddMinutes(3));

        Assert.Equal(CareHomeVisitStatus.Rescheduled, original.Status);
        Assert.Equal(CareHomeVisitStatus.Approved, replacement.Status);
    }

    [Fact]
    public void Cancellation_is_rejected_when_visit_has_started()
    {
        CareHomeVisit visit = MakeVisit(Now.AddHours(25));
        visit.Approve(UserId.New(), Now.AddMinutes(1));

        Assert.Throws<Sanad.BuildingBlocks.Domain.Exceptions.DomainException>(() =>
            visit.Cancel(false, UserId.New(), null, Now.AddHours(25)));
    }

    [Fact]
    public void Cairo_slots_require_both_schedules_respect_closures_and_reserve_pending_visitors()
    {
        TimeZoneInfo cairo = CareHomeVisitScheduleRules.CairoTimeZone;
        DateTime local = new(2026, 10, 12, 0, 0, 0, DateTimeKind.Unspecified);
        DateOnly date = DateOnly.FromDateTime(local);
        DateTime fromUtc = TimeZoneInfo.ConvertTimeToUtc(local, cairo);
        var window = new CareHomeVisitTimeWindow(date.DayOfWeek, new TimeOnly(10, 0), new TimeOnly(13, 0));
        CareHomeVisitSettings settings = CareHomeVisitSettings.Create(CareHomeId.New(), 4,
            [window], [window], [], Now);
        var reservation = CareHomeVisit.Request(settings.FacilityId, FamilyId.New(), UserId.New(),
            CareHomeVisitKind.Prospective, null, "Visitor", "+201000000000", 2,
            TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(date.ToDateTime(new TimeOnly(10, 0)), DateTimeKind.Unspecified), cairo), Now);
        IReadOnlyList<CareHomeVisitSlot> slots = CareHomeVisitScheduleRules.GetSlots(settings, date, 3, [reservation], Now);
        Assert.DoesNotContain(slots, x => x.StartsAt.UtcDateTime == reservation.StartsAtUtc);
        Assert.Contains(slots, x => x.StartsAt.UtcDateTime == fromUtc.AddHours(11));

        CareHomeVisitSettings closed = CareHomeVisitSettings.Create(settings.FacilityId, 4,
            [window], [window], [new CareHomeVisitClosure(date, "Closed")], Now);
        Assert.Empty(CareHomeVisitScheduleRules.GetSlots(closed, date, 1, [], Now));
    }

    [Fact]
    public void Family_and_owner_routes_have_separate_authorization_policies()
    {
        Type controller = typeof(CareHomeVisitsController);
        Assert.Equal("api/v1/care-homes", Assert.Single(controller.GetCustomAttributes<RouteAttribute>()).Template);
        Assert.Equal(AuthorizationPolicies.FamilyAccess,
            Assert.Single(controller.GetMethod(nameof(CareHomeVisitsController.FamilyCreate))!.GetCustomAttributes<AuthorizeAttribute>()).Policy);
        Assert.Equal(AuthorizationPolicies.CareHomeOwnerAccess,
            Assert.Single(controller.GetMethod(nameof(CareHomeVisitsController.OwnerDecision))!.GetCustomAttributes<AuthorizeAttribute>()).Policy);
        Assert.NotNull(controller.GetMethod(nameof(CareHomeVisitsController.Availability))!.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Equal(409, ResultProblemDetailsMapper.Create(CareHomeVisitErrors.CapacityConflict,
            new Microsoft.AspNetCore.Http.DefaultHttpContext()).Status);
        Assert.Equal(404, ResultProblemDetailsMapper.Create(CareHomeVisitErrors.NotFound,
            new Microsoft.AspNetCore.Http.DefaultHttpContext()).Status);
    }

    [Fact]
    public async Task Eligible_residents_returns_only_accepted_paid_unchecked_out_bookings_for_caller_family()
    {
        await using var db = new CareHomesDbContext(new DbContextOptionsBuilder<CareHomesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
        var facilityId = CareHomeId.New();
        var familyId = FamilyId.New();
        var otherFamilyId = FamilyId.New();
        var elderlyId = ElderlyId.New();
        var otherElderlyId = ElderlyId.New();

        var eligible = CareHomeBooking.Create(facilityId, UserId.New(), familyId, elderlyId, Guid.NewGuid(),
            new DateOnly(2026, 10, 10), 100m, 0m, 0m, 1, "Arabic", "English", 70, null,
            "Contact", null, null, null, Now);
        eligible.MarkPaid(1, Now);
        eligible.Accept(Now);

        var checkedOut = CareHomeBooking.Create(facilityId, UserId.New(), familyId, otherElderlyId, Guid.NewGuid(),
            new DateOnly(2026, 10, 10), 100m, 0m, 0m, 1, "Other", "Other", 71, null,
            "Contact", null, null, null, Now);
        checkedOut.MarkPaid(2, Now);
        checkedOut.Accept(Now);
        var property = typeof(CareHomeBooking).GetProperty("ActualCheckOutOnUtc", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        property.SetValue(checkedOut, Now.AddDays(1));

        var otherFamily = CareHomeBooking.Create(facilityId, UserId.New(), otherFamilyId, ElderlyId.New(), Guid.NewGuid(),
            new DateOnly(2026, 10, 10), 100m, 0m, 0m, 1, "Foreign", "Foreign", 72, null,
            "Contact", null, null, null, Now);
        otherFamily.MarkPaid(3, Now);
        otherFamily.Accept(Now);

        db.Bookings.AddRange(eligible, checkedOut, otherFamily);
        await db.SaveChangesAsync();

        var result = await new GetEligibleCareHomeVisitResidentsQueryHandler(db).Handle(
            new GetEligibleCareHomeVisitResidentsQuery(familyId, facilityId.Value, new DateTimeOffset(2026, 10, 10, 8, 0, 0, TimeSpan.Zero)),
            default);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Equal(elderlyId.Value, result.Value[0].ElderlyId);
        Assert.Equal("Arabic", result.Value[0].ArabicName);
        Assert.Equal("English", result.Value[0].EnglishName);
        Assert.Equal(eligible.Id, result.Value[0].CareHomeBookingId);
    }

    [Fact]
    public async Task Eligible_residents_invalid_request_for_empty_facility_or_default_startsAt()
    {
        await using var db = new CareHomesDbContext(new DbContextOptionsBuilder<CareHomesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
        var result = await new GetEligibleCareHomeVisitResidentsQueryHandler(db).Handle(
            new GetEligibleCareHomeVisitResidentsQuery(FamilyId.New(), Guid.Empty, default), default);
        Assert.False(result.IsSuccess);
        Assert.Equal("CareHomes.Visits.InvalidRequest", result.Error.Code);
    }

    [Fact]
    public async Task Eligible_residents_parity_create_accepts_exactly_the_read_result()
    {
        await using var db = new CareHomesDbContext(new DbContextOptionsBuilder<CareHomesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
        CareHomeFacility facility = CreateApprovedFacility(Now, new DateOnly(2026, 10, 20));
        db.Facilities.Add(facility);
        var window = new CareHomeVisitTimeWindow(DayOfWeek.Monday, new TimeOnly(8, 0), new TimeOnly(17, 0));
        db.VisitSettings.Add(CareHomeVisitSettings.Create(facility.Id, 4, [window], [window], [], Now));
        await db.SaveChangesAsync();

        var familyId = FamilyId.New();
        var eligibleElderly = ElderlyId.New();
        var ineligibleElderly = ElderlyId.New();

        var eligibleBooking = MakeEligibleBooking(facility.Id, familyId, eligibleElderly, new DateOnly(2026, 10, 12));
        var ineligibleBooking = CareHomeBooking.Create(facility.Id, UserId.New(), familyId, ineligibleElderly, Guid.NewGuid(),
            new DateOnly(2026, 10, 12), 100m, 0m, 0m, 1, "Ineligible", "Ineligible", 71, null, "Contact", null, null, null, Now);
        db.Bookings.AddRange(eligibleBooking, ineligibleBooking);
        await db.SaveChangesAsync();

        var startsAt = new DateTimeOffset(2026, 10, 12, 8, 0, 0, TimeSpan.Zero);
        var read = await new GetEligibleCareHomeVisitResidentsQueryHandler(db).Handle(
            new GetEligibleCareHomeVisitResidentsQuery(familyId, facility.Id.Value, startsAt), default);
        Assert.True(read.IsSuccess);
        Assert.Single(read.Value);
        Assert.Equal(eligibleElderly.Value, read.Value[0].ElderlyId);

        var createEligible = await new CreateCareHomeVisitCommandHandler(db, new InlineVisitGuard()).Handle(
            new CreateCareHomeVisitCommand(UserId.New(), familyId, facility.Id.Value, CareHomeVisitKind.Resident,
                eligibleElderly.Value, "Visitor", "+201000000000", 1, startsAt, Now), default);
        Assert.True(createEligible.IsSuccess);

        var createIneligible = await new CreateCareHomeVisitCommandHandler(db, new InlineVisitGuard()).Handle(
            new CreateCareHomeVisitCommand(UserId.New(), familyId, facility.Id.Value, CareHomeVisitKind.Resident,
                ineligibleElderly.Value, "Visitor", "+201000000000", 1, startsAt, Now), default);
        Assert.False(createIneligible.IsSuccess);
        Assert.Equal("CareHomes.Visits.NotFound", createIneligible.Error.Code);
    }

    [Fact]
    public async Task Eligible_residents_excludes_other_facility_not_accepted_not_paid_bad_window_and_checked_out()
    {
        await using var db = new CareHomesDbContext(new DbContextOptionsBuilder<CareHomesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
        var facilityId = CareHomeId.New();
        var otherFacilityId = CareHomeId.New();
        var familyId = FamilyId.New();

        var ok = MakeEligibleBooking(facilityId, familyId, ElderlyId.New(), new DateOnly(2026, 10, 10));

        var otherFacility = MakeEligibleBooking(otherFacilityId, familyId, ElderlyId.New(), new DateOnly(2026, 10, 10));

        var notAccepted = CareHomeBooking.Create(facilityId, UserId.New(), familyId, ElderlyId.New(), Guid.NewGuid(),
            new DateOnly(2026, 10, 10), 100m, 0m, 0m, 1, "NA", "NA", 70, null, "Contact", null, null, null, Now);

        var pendingPayment = CareHomeBooking.Create(facilityId, UserId.New(), familyId, ElderlyId.New(), Guid.NewGuid(),
            new DateOnly(2026, 10, 10), 100m, 0m, 0m, 1, "P", "P", 70, null, "Contact", null, null, null, Now);

        var paidNotAccepted = CareHomeBooking.Create(facilityId, UserId.New(), familyId, ElderlyId.New(), Guid.NewGuid(),
            new DateOnly(2026, 10, 10), 100m, 0m, 0m, 1, "PaidNotAccepted", "PaidNotAccepted", 70, null, "Contact", null, null, null, Now);
        paidNotAccepted.MarkPaid(99, Now);

        var futureStart = MakeEligibleBooking(facilityId, familyId, ElderlyId.New(), new DateOnly(2026, 10, 11));

        var pastEnd = MakeEligibleBooking(facilityId, familyId, ElderlyId.New(), new DateOnly(2026, 10, 9));
        // make EndDate equal to local date by forcing a one-day booking via reflection not needed; instead adjust via a booking with StartDate 10-09 EndDate 10-10 already has EndDate > localDate for local 10-09, so use reflection to set EndDate = local date
        // Create always sets EndDate = StartDate.AddMonths(1); there is no public
        // method for an arbitrary end date, so reflection is the only way to
        // exercise the EndDate > localDate boundary.
        var property = typeof(CareHomeBooking).GetProperty("EndDate", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        property.SetValue(pastEnd, new DateOnly(2026, 10, 10));

        var checkedOut = MakeEligibleBooking(facilityId, familyId, ElderlyId.New(), new DateOnly(2026, 10, 10));
        checkedOut.AssignPhysicalResource(Guid.NewGuid(), null, Now);
        checkedOut.RecordCheckIn(UserId.New(), Now);
        checkedOut.RecordCheckOut(UserId.New(), Now.AddDays(1));

        db.Bookings.AddRange(ok, otherFacility, notAccepted, pendingPayment, paidNotAccepted, futureStart, pastEnd, checkedOut);
        await db.SaveChangesAsync();

        var result = await new GetEligibleCareHomeVisitResidentsQueryHandler(db).Handle(
            new GetEligibleCareHomeVisitResidentsQuery(familyId, facilityId.Value, new DateTimeOffset(2026, 10, 10, 8, 0, 0, TimeSpan.Zero)), default);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Equal(ok.ElderlyId.Value, result.Value[0].ElderlyId);
    }

    [Fact]
    public async Task Eligible_residents_returns_deterministic_order_for_multiple_residents()
    {
        await using var db = new CareHomesDbContext(new DbContextOptionsBuilder<CareHomesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
        var facilityId = CareHomeId.New();
        var familyId = FamilyId.New();
        var z = CareHomeBooking.Create(facilityId, UserId.New(), familyId, ElderlyId.New(), Guid.NewGuid(),
            new DateOnly(2026, 10, 10), 100m, 0m, 0m, 1, "Zeta", "Zeta", 70, null, "Contact", null, null, null, Now);
        z.MarkPaid(1, Now);
        z.Accept(Now);
        var a = CareHomeBooking.Create(facilityId, UserId.New(), familyId, ElderlyId.New(), Guid.NewGuid(),
            new DateOnly(2026, 10, 10), 100m, 0m, 0m, 1, "Alpha", "Alpha", 70, null, "Contact", null, null, null, Now);
        a.MarkPaid(2, Now);
        a.Accept(Now);
        db.Bookings.AddRange(z, a);
        await db.SaveChangesAsync();

        var result = await new GetEligibleCareHomeVisitResidentsQueryHandler(db).Handle(
            new GetEligibleCareHomeVisitResidentsQuery(familyId, facilityId.Value, new DateTimeOffset(2026, 10, 10, 8, 0, 0, TimeSpan.Zero)), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.Equal("Alpha", result.Value[0].ArabicName);
        Assert.Equal("Zeta", result.Value[1].ArabicName);
    }

    [Fact]
    public async Task Eligible_residents_invalid_request_for_empty_facility()
    {
        await using var db = new CareHomesDbContext(new DbContextOptionsBuilder<CareHomesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
        var result = await new GetEligibleCareHomeVisitResidentsQueryHandler(db).Handle(
            new GetEligibleCareHomeVisitResidentsQuery(FamilyId.New(), Guid.Empty, new DateTimeOffset(2026, 10, 10, 8, 0, 0, TimeSpan.Zero)), default);
        Assert.False(result.IsSuccess);
        Assert.Equal("CareHomes.Visits.InvalidRequest", result.Error.Code);
    }

    [Fact]
    public async Task Eligible_residents_invalid_request_for_default_startsAt()
    {
        await using var db = new CareHomesDbContext(new DbContextOptionsBuilder<CareHomesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
        var result = await new GetEligibleCareHomeVisitResidentsQueryHandler(db).Handle(
            new GetEligibleCareHomeVisitResidentsQuery(FamilyId.New(), Guid.NewGuid(), default), default);
        Assert.False(result.IsSuccess);
        Assert.Equal("CareHomes.Visits.InvalidRequest", result.Error.Code);
    }

    [Fact]
    public void Eligible_residents_route_uses_FamilyAccess_policy()
    {
        var method = typeof(CareHomeVisitsController).GetMethod(nameof(CareHomeVisitsController.FamilyEligibleResidents))!;
        Assert.Equal(AuthorizationPolicies.FamilyAccess,
            Assert.Single(method.GetCustomAttributes<AuthorizeAttribute>()).Policy);
    }

    private static CareHomeBooking MakeEligibleBooking(CareHomeId facilityId, FamilyId familyId, ElderlyId elderlyId, DateOnly startDate)
    {
        var booking = CareHomeBooking.Create(facilityId, UserId.New(), familyId, elderlyId, Guid.NewGuid(),
            startDate, 100m, 0m, 0m, 1, "Arabic", "English", 70, null, "Contact", null, null, null, Now);
        booking.MarkPaid(1, Now);
        booking.Accept(Now);
        return booking;
    }

    private sealed class InlineVisitGuard : ICareHomeVisitReservationGuard
    {
        public Task<T> ExecuteAsync<T>(CareHomeId facilityId, Func<Task<T>> action, CancellationToken cancellationToken) => action();
    }

    private static CareHomeFacility CreateApprovedFacility(DateTime now, DateOnly licenseExpiry)
    {
        UserId owner = UserId.New();
        UserId admin = UserId.New();
        CareHomeFacility facility = CareHomeFacility.CreateDraft(owner, now);
        facility.SaveDraft(owner, facility.Version, CareHomeFacilityTests.Draft(), now);
        foreach (CareHomeDocumentType type in Enum.GetValues<CareHomeDocumentType>())
            facility.UploadDocument(owner, type, $"{type}-{Guid.NewGuid():N}.pdf", "application/pdf", 100,
                type == CareHomeDocumentType.OperatingLicense ? licenseExpiry : null, now.AddSeconds(1));
        facility.Submit(owner, facility.Version, now.AddMinutes(1));
        foreach (CareHomeDocument document in facility.Documents)
            facility.VerifyDocument(admin, facility.Version, document.Id,
                document.Type == CareHomeDocumentType.OperatingLicense ? licenseExpiry : null,
                document.Type != CareHomeDocumentType.OperatingLicense, now.AddMinutes(2));
        facility.Review(admin, facility.Version, CareHomeReviewAction.Approved, null, now.AddMinutes(3),
            DateOnly.FromDateTime(now).AddDays(-2));
        return facility;
    }

    private static CareHomeVisit MakeVisit(DateTime startsAt) => CareHomeVisit.Request(
        CareHomeId.New(), FamilyId.New(), UserId.New(), CareHomeVisitKind.Prospective, null,
        "Visitor", "+201000000000", 2, startsAt, Now);
}
