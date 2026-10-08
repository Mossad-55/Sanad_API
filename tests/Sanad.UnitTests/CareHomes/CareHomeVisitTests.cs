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

    private static CareHomeVisit MakeVisit(DateTime startsAt) => CareHomeVisit.Request(
        CareHomeId.New(), FamilyId.New(), UserId.New(), CareHomeVisitKind.Prospective, null,
        "Visitor", "+201000000000", 2, startsAt, Now);
}
