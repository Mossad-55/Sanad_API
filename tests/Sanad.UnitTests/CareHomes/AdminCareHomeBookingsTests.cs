using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sanad.API.Authorization;
using Sanad.API.Controllers;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Application.Bookings;
using Sanad.Modules.CareHomes.Domain.Bookings;
using Sanad.Modules.CareHomes.Domain.Facilities;
using Sanad.Modules.CareHomes.Infrastructure.Persistence;

namespace Sanad.UnitTests.CareHomes;

public sealed class AdminCareHomeBookingsTests
{
    private static readonly DateTime Now = new(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task List_filters_overlap_status_facility_and_exact_search_then_pages_stably()
    {
        await using var db = CreateDb();
        CareHomeId facility = CareHomeId.New();
        CareHomeBooking older = CreateBooking(facility, Now.AddDays(-2));
        CareHomeBooking newer = CreateBooking(facility, Now.AddDays(-1));
        CareHomeBooking other = CreateBooking(CareHomeId.New(), Now);
        newer.MarkPaid(123, Now.AddMinutes(1));
        db.Bookings.AddRange(older, newer, other);
        await db.SaveChangesAsync();

        var handler = new ListAdminCareHomeBookingsHandler(db);
        var first = await handler.Handle(new(1, 1, facility.Value,
            PaymentStatus: CareHomeBookingPaymentStatus.Paid,
            StayFrom: new DateOnly(2026, 10, 31), StayTo: new DateOnly(2026, 11, 1)), default);
        var exact = await handler.Handle(new(FacilityId: facility.Value, Search: newer.MerchantReference), default);

        Assert.True(first.IsSuccess);
        Assert.Equal(1, first.Value.TotalCount);
        Assert.Equal(newer.Id, first.Value.Items[0].BookingId);
        Assert.True(exact.IsSuccess);
        Assert.Single(exact.Value.Items);
        Assert.Equal(newer.Id, exact.Value.Items[0].BookingId);
        Assert.Equal("EGP", exact.Value.Items[0].Currency);
    }

    [Fact]
    public async Task List_rejects_invalid_paging_status_and_range()
    {
        await using var db = CreateDb();
        var handler = new ListAdminCareHomeBookingsHandler(db);
        Assert.Equal("CareHomes.Admin.InvalidQuery", (await handler.Handle(new(Page: 0), default)).Error.Code);
        Assert.Equal("CareHomes.Admin.InvalidQuery", (await handler.Handle(new(PageSize: 101), default)).Error.Code);
        Assert.Equal("CareHomes.Admin.InvalidQuery", (await handler.Handle(new(BookingStatus: (CareHomeBookingStatus)999), default)).Error.Code);
        Assert.Equal("CareHomes.Admin.InvalidQuery", (await handler.Handle(new(StayFrom: new(2026, 11, 2), StayTo: new(2026, 11, 1)), default)).Error.Code);
    }

    [Fact]
    public async Task Detail_includes_operational_facts_and_history_without_sensitive_intake_or_payment_secrets()
    {
        await using var db = CreateDb();
        CareHomeId facility = CareHomeId.New();
        UserId actor = UserId.New();
        CareHomeBooking booking = CareHomeBooking.Create(facility, UserId.New(), FamilyId.New(), ElderlyId.New(),
            Guid.NewGuid(), new DateOnly(2026, 10, 10), 1000m, 100m, 50m, 4, "مقيم", "Resident", 80,
            "MEDICAL_SECRET", "Private Contact", "01000000000", "son", "CARE_NOTES_SECRET", Now);
        booking.SetPaymentIntent(1, "PROVIDER_ORDER_SECRET", "INTENTION_SECRET", "CLIENT_SECRET", "PUBLIC_KEY_SECRET", Now);
        booking.MarkPaid(456, Now.AddMinutes(1));
        booking.Accept(Now.AddMinutes(2));
        Guid roomId = Guid.NewGuid();
        booking.AssignPhysicalResource(roomId, null, Now.AddMinutes(3));
        db.Bookings.Add(booking);
        db.BookingAssignmentHistory.Add(CareHomeBookingAssignmentHistory.CreateInitial(
            booking.Id, roomId, null, booking.StartDate, actor, Now.AddMinutes(3)));
        var dispute = CareHomeCheckInDispute.Open(booking.Id, facility, null, actor, Now.AddMinutes(4), "Issue");
        db.CheckInDisputes.Add(dispute);
        await db.SaveChangesAsync();

        var result = await new GetAdminCareHomeBookingHandler(db).Handle(new(booking.Id), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(1150m, result.Value.TotalAmount);
        Assert.Single(result.Value.AssignmentHistory);
        Assert.Equal(dispute.Id, result.Value.CheckInDispute!.DisputeId);
        string json = JsonSerializer.Serialize(result.Value);
        foreach (string excluded in new[] { "MEDICAL_SECRET", "CARE_NOTES_SECRET", "01000000000",
                     "CLIENT_SECRET", "PUBLIC_KEY_SECRET", "PROVIDER_ORDER_SECRET", "INTENTION_SECRET" })
            Assert.DoesNotContain(excluded, json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Detail_returns_not_found_for_unknown_booking()
    {
        await using var db = CreateDb();
        var result = await new GetAdminCareHomeBookingHandler(db).Handle(new(Guid.NewGuid()), default);
        Assert.Equal("CareHomes.Bookings.NotFound", result.Error.Code);
    }

    [Fact]
    public void Routes_use_operational_admin_policy_and_expected_paths()
    {
        Type controller = typeof(AdminCareHomeBookingsController);
        Assert.Equal(AuthorizationPolicies.CareHomesOperationalAdmin,
            Assert.Single(controller.GetCustomAttributes<AuthorizeAttribute>()).Policy);
        Assert.Equal("api/v1/admin/care-homes/bookings", Assert.Single(controller.GetCustomAttributes<RouteAttribute>()).Template);
        Assert.Null(Assert.Single(controller.GetMethod(nameof(AdminCareHomeBookingsController.List))!
            .GetCustomAttributes<HttpGetAttribute>()).Template);
        Assert.Equal("{bookingId:guid}", Assert.Single(controller.GetMethod(nameof(AdminCareHomeBookingsController.Detail))!
            .GetCustomAttributes<HttpGetAttribute>()).Template);
    }

    private static CareHomeBooking CreateBooking(CareHomeId facility, DateTime created) =>
        CareHomeBooking.Create(facility, UserId.New(), FamilyId.New(), ElderlyId.New(), Guid.NewGuid(),
            new DateOnly(2026, 10, 10), 1000m, 100m, 50m, 4, "مقيم", "Resident", 80,
            null, "Contact", null, null, null, created);

    private static CareHomesDbContext CreateDb() => new(new DbContextOptionsBuilder<CareHomesDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
