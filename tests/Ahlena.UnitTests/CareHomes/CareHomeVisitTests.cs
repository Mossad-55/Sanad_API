using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;
using Ahlena.API.Authorization;
using Ahlena.API.Controllers;
using Ahlena.API.ProblemDetail;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.CareHomes.Application.Abstractions.Data;
using Ahlena.Modules.CareHomes.Application.Visits;
using Ahlena.Modules.CareHomes.Domain.Bookings;
using Ahlena.Modules.CareHomes.Domain.Facilities;
using Ahlena.Modules.CareHomes.Domain.Idempotency;
using Ahlena.Modules.CareHomes.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Ahlena.Modules.Families.Domain.Families;

namespace Ahlena.UnitTests.CareHomes;

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
        Assert.Throws<Ahlena.BuildingBlocks.Domain.Exceptions.DomainException>(() => visit.Approve(UserId.New(), Now.AddHours(24)));
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

        Assert.Throws<Ahlena.BuildingBlocks.Domain.Exceptions.DomainException>(() =>
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

    // ---------------------------------------------------------------------
    // P2 optional visit-create idempotency (`Idempotency-Key` header).
    // ---------------------------------------------------------------------

    private static readonly DateTimeOffset VisitStartsAt =
        new(2026, 10, 12, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Create_is_idempotent_for_the_same_key_and_the_same_content()
    {
        (CareHomesDbContext db, CareHomeFacility facility) = await CreateVisitScenarioAsync();
        var familyId = FamilyId.New();
        CreateCareHomeVisitCommandHandler handler = CreateVisitHandler(db);

        Result<CareHomeVisitResponse> first = await handler.Handle(
            CreateVisitCommand(facility, familyId, "key-1"), default);
        // A sequential retry, including one with untrimmed inputs, replays the
        // stored visit instead of creating a second row.
        Result<CareHomeVisitResponse> retry = await handler.Handle(
            CreateVisitCommand(facility, familyId, "key-1", visitorName: "  Visitor  "), default);

        Assert.True(first.IsSuccess);
        Assert.True(retry.IsSuccess);
        Assert.Equal(first.Value!.Id, retry.Value!.Id);
        Assert.Single(await db.Visits.ToListAsync());
        Assert.Equal("key-1", (await db.Visits.SingleAsync()).IdempotencyKey);
    }

    [Fact]
    public async Task Create_conflicts_when_the_same_key_is_reused_with_different_content()
    {
        (CareHomesDbContext db, CareHomeFacility facility) = await CreateVisitScenarioAsync();
        var familyId = FamilyId.New();
        CreateCareHomeVisitCommandHandler handler = CreateVisitHandler(db);

        Result<CareHomeVisitResponse> first = await handler.Handle(
            CreateVisitCommand(facility, familyId, "key-1"), default);
        Assert.True(first.IsSuccess);

        var differingCases = new (string Case, CreateCareHomeVisitCommand Rival)[]
        {
            ("visitor-name", CreateVisitCommand(facility, familyId, "key-1", visitorName: "Other visitor")),
            ("visitor-count", CreateVisitCommand(facility, familyId, "key-1", visitorCount: 2)),
            ("starts-at", CreateVisitCommand(facility, familyId, "key-1", startsAt: VisitStartsAt.AddHours(1))),
            ("kind-and-elderly", new CreateCareHomeVisitCommand(UserId.New(), familyId, facility.Id.Value,
                CareHomeVisitKind.Resident, ElderlyId.New().Value, "Visitor", "+201000000000", 1, VisitStartsAt, Now, "key-1")),
        };

        foreach (var (caseName, rival) in differingCases)
        {
            Result<CareHomeVisitResponse> conflicting = await handler.Handle(rival, default);
            Assert.True(conflicting.IsFailure, caseName);
            Assert.Equal("CareHomes.Visits.IdempotencyConflict", conflicting.Error!.Code);
            Assert.Equal(409, ResultProblemDetailsMapper.Create(conflicting.Error,
                new Microsoft.AspNetCore.Http.DefaultHttpContext()).Status);
        }

        // One row per key: the conflicting retries stored nothing.
        Assert.Single(await db.Visits.ToListAsync());
        Assert.Equal(first.Value!.Id, (await db.Visits.SingleAsync()).Id);
    }

    [Fact]
    public async Task Create_without_a_key_keeps_the_legacy_duplicate_behavior()
    {
        (CareHomesDbContext db, CareHomeFacility facility) = await CreateVisitScenarioAsync();
        var familyId = FamilyId.New();
        CreateCareHomeVisitCommandHandler handler = CreateVisitHandler(db);

        Result<CareHomeVisitResponse> first = await handler.Handle(
            CreateVisitCommand(facility, familyId), default);
        Result<CareHomeVisitResponse> second = await handler.Handle(
            CreateVisitCommand(facility, familyId), default);

        // No header: two identical legal creates still create two rows and no
        // idempotency key is stored — exactly the pre-P2 behavior.
        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.NotEqual(first.Value!.Id, second.Value!.Id);
        Assert.All(await db.Visits.ToListAsync(), visit => Assert.Null(visit.IdempotencyKey));
    }

    [Fact]
    public async Task Create_treats_a_blank_or_whitespace_key_as_absent()
    {
        (CareHomesDbContext db, CareHomeFacility facility) = await CreateVisitScenarioAsync();
        var familyId = FamilyId.New();
        CreateCareHomeVisitCommandHandler handler = CreateVisitHandler(db);

        Result<CareHomeVisitResponse> first = await handler.Handle(
            CreateVisitCommand(facility, familyId, "   "), default);
        Result<CareHomeVisitResponse> second = await handler.Handle(
            CreateVisitCommand(facility, familyId, "\t"), default);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.NotEqual(first.Value!.Id, second.Value!.Id);
        Assert.All(await db.Visits.ToListAsync(), visit => Assert.Null(visit.IdempotencyKey));
    }

    [Fact]
    public async Task Create_scopes_the_key_per_family()
    {
        (CareHomesDbContext db, CareHomeFacility facility) = await CreateVisitScenarioAsync();
        CreateCareHomeVisitCommandHandler handler = CreateVisitHandler(db);

        Result<CareHomeVisitResponse> first = await handler.Handle(
            CreateVisitCommand(facility, FamilyId.New(), "shared-key"), default);
        Result<CareHomeVisitResponse> second = await handler.Handle(
            CreateVisitCommand(facility, FamilyId.New(), "shared-key"), default);

        // The same key from another Family is a different scope: both creates
        // succeed and both rows exist.
        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(2, await db.Visits.CountAsync());
    }

    [Fact]
    public async Task Create_rejects_a_key_over_the_pinned_bound_and_accepts_the_boundary()
    {
        (CareHomesDbContext db, CareHomeFacility facility) = await CreateVisitScenarioAsync();
        CreateCareHomeVisitCommandHandler handler = CreateVisitHandler(db);

        Result<CareHomeVisitResponse> over = await handler.Handle(
            CreateVisitCommand(facility, FamilyId.New(), new string('k', CareHomeVisit.MaximumIdempotencyKeyLength + 1)), default);

        Assert.True(over.IsFailure);
        Assert.Equal("CareHomes.Visits.InvalidRequest", over.Error!.Code);
        Assert.Empty(await db.Visits.ToListAsync());

        Result<CareHomeVisitResponse> atBound = await handler.Handle(
            CreateVisitCommand(facility, FamilyId.New(), new string('k', CareHomeVisit.MaximumIdempotencyKeyLength)), default);

        Assert.True(atBound.IsSuccess);
        Assert.Single(await db.Visits.ToListAsync());
    }

    [Fact]
    public async Task Create_returns_the_persisted_winner_when_a_raced_retry_lost_the_unique_index()
    {
        string databaseName = Guid.NewGuid().ToString("N");
        (CareHomeFacility facility, FamilyId familyId) = await SeedVisitScenarioAsync(databaseName);
        var familyUserId = UserId.New();
        var racing = new RacingCareHomesDbContext(
            databaseName,
            winnerDb => winnerDb.Visits.Add(CareHomeVisit.Request(
                facility.Id, familyId, familyUserId, CareHomeVisitKind.Prospective, null,
                "Visitor", "+201000000000", 1, VisitStartsAt.UtcDateTime, Now, "key-1")));

        Result<CareHomeVisitResponse> result = await CreateVisitHandler(racing).Handle(
            new CreateCareHomeVisitCommand(familyUserId, familyId, facility.Id.Value, CareHomeVisitKind.Prospective,
                null, "Visitor", "+201000000000", 1, VisitStartsAt, Now, "key-1"), default);

        // The raced-out request answers with the winner's visit, and the
        // failed insert left nothing behind: the store holds the winner only.
        CareHomeVisit winner = await racing.Inner.Visits.SingleAsync();
        Assert.True(result.IsSuccess);
        Assert.Equal(winner.Id, result.Value!.Id);
        Assert.Equal(1, await racing.Inner.Visits.CountAsync());
    }

    [Fact]
    public async Task Create_conflicts_when_a_raced_retry_won_with_different_content()
    {
        string databaseName = Guid.NewGuid().ToString("N");
        (CareHomeFacility facility, FamilyId familyId) = await SeedVisitScenarioAsync(databaseName);
        var racing = new RacingCareHomesDbContext(
            databaseName,
            winnerDb => winnerDb.Visits.Add(CareHomeVisit.Request(
                facility.Id, familyId, UserId.New(), CareHomeVisitKind.Prospective, null,
                "Visitor", "+201000000000", 2, VisitStartsAt.UtcDateTime, Now, "key-1")));

        Result<CareHomeVisitResponse> result = await CreateVisitHandler(racing).Handle(
            new CreateCareHomeVisitCommand(UserId.New(), familyId, facility.Id.Value, CareHomeVisitKind.Prospective,
                null, "Visitor", "+201000000000", 1, VisitStartsAt, Now, "key-1"), default);

        Assert.True(result.IsFailure);
        Assert.Equal("CareHomes.Visits.IdempotencyConflict", result.Error!.Code);
        Assert.Equal(1, await racing.Inner.Visits.CountAsync());
    }

    [Fact]
    public async Task Create_propagates_a_unique_violation_when_no_winner_was_persisted()
    {
        string databaseName = Guid.NewGuid().ToString("N");
        (CareHomeFacility facility, FamilyId familyId) = await SeedVisitScenarioAsync(databaseName);
        var racing = new RacingCareHomesDbContext(databaseName, persistWinner: null);

        // No winner exists: the failure must not be reported as a success.
        await Assert.ThrowsAsync<DbUpdateException>(() => CreateVisitHandler(racing).Handle(
            new CreateCareHomeVisitCommand(UserId.New(), familyId, facility.Id.Value, CareHomeVisitKind.Prospective,
                null, "Visitor", "+201000000000", 1, VisitStartsAt, Now, "key-1"), default));

        Assert.Empty(racing.Inner.Visits);
    }

    [Fact]
    public void Family_create_binds_the_optional_idempotency_header()
    {
        System.Reflection.ParameterInfo parameter = typeof(CareHomeVisitsController)
            .GetMethod(nameof(CareHomeVisitsController.FamilyCreate))!
            .GetParameters()
            .Single(item => item.Name == "idempotencyKey");

        FromHeaderAttribute header = parameter.GetCustomAttribute<FromHeaderAttribute>()!;
        Assert.Equal("Idempotency-Key", header.Name);
        Assert.Equal(409, ResultProblemDetailsMapper.Create(CareHomeVisitErrors.IdempotencyConflict,
            new Microsoft.AspNetCore.Http.DefaultHttpContext()).Status);
    }

    private static async Task<(CareHomesDbContext Db, CareHomeFacility Facility)> CreateVisitScenarioAsync()
    {
        var db = new CareHomesDbContext(new DbContextOptionsBuilder<CareHomesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
        CareHomeFacility facility = await SeedVisitScenarioAsync(db);
        return (db, facility);
    }

    private static async Task<(CareHomeFacility Facility, FamilyId FamilyId)> SeedVisitScenarioAsync(string databaseName)
    {
        var db = new CareHomesDbContext(new DbContextOptionsBuilder<CareHomesDbContext>()
            .UseInMemoryDatabase(databaseName).Options);
        CareHomeFacility facility = await SeedVisitScenarioAsync(db);
        return (facility, FamilyId.New());
    }

    private static async Task<CareHomeFacility> SeedVisitScenarioAsync(CareHomesDbContext db)
    {
        CareHomeFacility facility = CreateApprovedFacility(Now, new DateOnly(2026, 10, 20));
        db.Facilities.Add(facility);
        var window = new CareHomeVisitTimeWindow(DayOfWeek.Monday, new TimeOnly(8, 0), new TimeOnly(17, 0));
        db.VisitSettings.Add(CareHomeVisitSettings.Create(facility.Id, 4, [window], [window], [], Now));
        await db.SaveChangesAsync();
        return facility;
    }

    private static CreateCareHomeVisitCommand CreateVisitCommand(
        CareHomeFacility facility,
        FamilyId familyId,
        string? idempotencyKey = null,
        int visitorCount = 1,
        string visitorName = "Visitor",
        DateTimeOffset? startsAt = null) =>
        new(UserId.New(), familyId, facility.Id.Value, CareHomeVisitKind.Prospective, null,
            visitorName, "+201000000000", visitorCount, startsAt ?? VisitStartsAt, Now, idempotencyKey);

    private static CreateCareHomeVisitCommandHandler CreateVisitHandler(ICareHomesDbContext db) =>
        new(db, new InlineVisitGuard());

    /// <summary>
    /// Simulates the persistence race on the unique (family id, idempotency
    /// key) index: the first save is rejected by the store, optionally after a
    /// concurrent request persisted the winning row through its own context
    /// over the same store. Mirrors the partner-contact race double.
    /// </summary>
    private sealed class RacingCareHomesDbContext(
        string databaseName,
        Action<CareHomesDbContext>? persistWinner) : ICareHomesDbContext
    {
        private readonly CareHomesDbContext _inner = new(new DbContextOptionsBuilder<CareHomesDbContext>()
            .UseInMemoryDatabase(databaseName).Options);
        private readonly CareHomesDbContext _winnerScope = new(new DbContextOptionsBuilder<CareHomesDbContext>()
            .UseInMemoryDatabase(databaseName).Options);
        private int _saves;

        public CareHomesDbContext Inner => _inner;

        public DbSet<CareHomeFacility> Facilities => _inner.Facilities;

        public DbSet<CareHomeRoomType> RoomTypes => _inner.RoomTypes;

        public DbSet<CareHomeRoomTypeMedia> RoomTypeMedia => _inner.RoomTypeMedia;

        public DbSet<CareHomeRoom> Rooms => _inner.Rooms;

        public DbSet<CareHomeRoomMedia> RoomMedia => _inner.RoomMedia;

        public DbSet<CareHomeBed> Beds => _inner.Beds;

        public DbSet<CareHomeMaintenanceBlock> MaintenanceBlocks => _inner.MaintenanceBlocks;

        public DbSet<CareHomeBooking> Bookings => _inner.Bookings;

        public DbSet<CareHomeInternalBookingNote> InternalBookingNotes => _inner.InternalBookingNotes;

        public DbSet<CareHomeRating> Ratings => _inner.Ratings;

        public DbSet<CareHomeRatingModerationAction> RatingModerationActions => _inner.RatingModerationActions;

        public DbSet<CareHomeRatingFlag> RatingFlags => _inner.RatingFlags;

        public DbSet<CareHomePayout> Payouts => _inner.Payouts;

        public DbSet<CareHomePayoutDebt> PayoutDebts => _inner.PayoutDebts;

        public DbSet<CareHomeBookingAssignmentHistory> BookingAssignmentHistory => _inner.BookingAssignmentHistory;

        public DbSet<CareHomeTransferNotificationOutbox> TransferNotificationOutbox => _inner.TransferNotificationOutbox;

        public DbSet<CareHomeNotificationOutbox> BookingNotificationOutbox => _inner.BookingNotificationOutbox;

        public DbSet<CareHomeCheckInDispute> CheckInDisputes => _inner.CheckInDisputes;

        public DbSet<CareHomeProfileMedia> ProfileMedia => _inner.ProfileMedia;

        public DbSet<CareHomeVisitSettings> VisitSettings => _inner.VisitSettings;

        public DbSet<CareHomeVisit> Visits => _inner.Visits;

        public DbSet<IdempotencyRequest> IdempotencyRequests => _inner.IdempotencyRequests;

        public void DetachAdded<TEntity>() where TEntity : class => _inner.DetachAdded<TEntity>();

        public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _saves) != 1)
            {
                return await _inner.SaveChangesAsync(cancellationToken);
            }

            if (persistWinner is not null)
            {
                persistWinner(_winnerScope);
                await _winnerScope.SaveChangesAsync(cancellationToken);
            }

            throw new DbUpdateException(
                "23505: duplicate key value violates unique constraint " +
                "\"IX_visits_family_id_idempotency_key\"",
                new InvalidOperationException("23505 duplicate key"));
        }
    }

    private static CareHomeBooking MakeEligibleBooking(CareHomeId facilityId, FamilyId familyId, ElderlyId elderlyId, DateOnly startDate)
    {
        var booking = CareHomeBooking.Create(facilityId, UserId.New(), familyId, elderlyId, Guid.NewGuid(),
            startDate, 100m, 0m, 0m, 1, "Arabic", "English", 70, null, "Contact", null, null, null, Now);
        booking.MarkPaid(1, Now);
        booking.Accept(Now);
        return booking;
    }

    /// <summary>
    /// Reason-cap acceptance facts for the frontend-report OpenAPI task: the
    /// decision/cancellation reason caps are measured on the TRIMMED text
    /// (whitespace-padded values above the raw cap are accepted), the approve
    /// path ignores the decision reason entirely, and the closure reason alone
    /// is measured RAW before trimming (hence the published maxLength: 500).
    /// </summary>
    [Fact]
    public async Task Owner_decision_approve_with_over_long_reason_is_accepted_ignored()
    {
        (CareHomesDbContext db, CareHomeFacility facility) = await CreateVisitScenarioAsync();
        var familyId = FamilyId.New();
        CreateCareHomeVisitCommandHandler create = CreateVisitHandler(db);
        Assert.True((await create.Handle(CreateVisitCommand(facility, familyId), default)).IsSuccess);
        Guid visitId = db.Visits.Single().Id;

        var result = await new DecideOwnerCareHomeVisitCommandHandler(db, new InlineVisitGuard()).Handle(
            new(facility.OwnerUserId, visitId, true, new string('x', 5000), Now.AddMinutes(5)),
            default);

        Assert.True(result.IsSuccess, result.Error?.Code);
        Assert.Equal(CareHomeVisitStatus.Approved, db.Visits.Single().Status);
    }

    [Fact]
    public async Task Owner_decision_reject_accepts_whitespace_padded_reason_up_to_the_trimmed_cap()
    {
        (CareHomesDbContext db, CareHomeFacility facility) = await CreateVisitScenarioAsync();
        var familyId = FamilyId.New();
        Assert.True((await CreateVisitHandler(db).Handle(CreateVisitCommand(facility, familyId), default)).IsSuccess);
        Guid visitId = db.Visits.Single().Id;

        // Raw 1005 characters, trimmed 995: accepted under the existing domain
        // cap of 1000 on the trimmed text and stored trimmed.
        string padded = new string('x', 995) + new string(' ', 10);
        var result = await new DecideOwnerCareHomeVisitCommandHandler(db, new InlineVisitGuard()).Handle(
            new(facility.OwnerUserId, visitId, false, padded, Now.AddMinutes(5)),
            default);

        Assert.True(result.IsSuccess, result.Error?.Code);
        CareHomeVisit stored = db.Visits.Single();
        Assert.Equal(995, stored.DecisionReason!.Length);
    }

    [Fact]
    public async Task Owner_decision_reject_still_refuses_a_reason_over_the_trimmed_cap()
    {
        (CareHomesDbContext db, CareHomeFacility facility) = await CreateVisitScenarioAsync();
        var familyId = FamilyId.New();
        Assert.True((await CreateVisitHandler(db).Handle(CreateVisitCommand(facility, familyId), default)).IsSuccess);
        Guid visitId = db.Visits.Single().Id;

        var result = await new DecideOwnerCareHomeVisitCommandHandler(db, new InlineVisitGuard()).Handle(
            new(facility.OwnerUserId, visitId, false, new string('x', 1001), Now.AddMinutes(5)),
            default);

        Assert.True(result.IsFailure);
        Assert.Equal(CareHomeVisitErrors.InvalidState.Code, result.Error.Code);
    }

    [Fact]
    public async Task Family_cancel_accepts_whitespace_padded_reason_up_to_the_trimmed_cap()
    {
        (CareHomesDbContext db, CareHomeFacility facility) = await CreateVisitScenarioAsync();
        var familyId = FamilyId.New();
        Assert.True((await CreateVisitHandler(db).Handle(CreateVisitCommand(facility, familyId), default)).IsSuccess);
        Guid visitId = db.Visits.Single().Id;

        string padded = new string('x', 995) + new string(' ', 10);
        var result = await new CancelFamilyCareHomeVisitCommandHandler(db, new InlineVisitGuard()).Handle(
            new(familyId, visitId, UserId.New(), padded, Now.AddMinutes(5)),
            default);

        Assert.True(result.IsSuccess, result.Error?.Code);
        Assert.Equal(995, db.Visits.Single().CancellationReason!.Length);
    }

    [Fact]
    public void Settings_closure_reason_is_measured_on_the_raw_value_before_trimming()
    {
        var window = new CareHomeVisitTimeWindow(DayOfWeek.Monday, new TimeOnly(8, 0), new TimeOnly(17, 0));
        CareHomeId facilityId = CareHomeId.New();
        DateOnly date = new(2026, 10, 12);

        // Raw exactly 500 (including trailing whitespace) is accepted...
        CareHomeVisitSettings.Create(facilityId, 4, [window], [window],
            [new CareHomeVisitClosure(date, new string('x', 495) + new string(' ', 5))], Now);

        // ...and raw 501 is refused before any trimming — the exact contract
        // the retained maxLength: 500 schema annotation states.
        Assert.Throws<Ahlena.BuildingBlocks.Domain.Exceptions.DomainException>(() =>
            CareHomeVisitSettings.Create(facilityId, 4, [window], [window],
                [new CareHomeVisitClosure(date, new string('x', 496) + new string(' ', 5))], Now));
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
