using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Domain.Bookings;
using Sanad.Modules.CareHomes.Domain.Facilities;
using Sanad.Modules.CareHomes.Infrastructure.Persistence;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.UnitTests.API;

/// <summary>
/// PORTAL-05 fixture-backed verification of the seven owner GET routes and the
/// visit-settings PUT through the isolated in-process HTTP host with production
/// auth/policies, MVC/JSON and real MediatR dispatch. Only the PostgreSQL
/// advisory-lock visit guard is inlined; occupancy, handlers and policies are
/// real. Each test seeds the minimum synthetic state, reads it back, then sends
/// HTTP scenarios. No database network, no production target, no provider sends.
/// </summary>
public sealed class CareHomeOwnerPortalTests : IClassFixture<PortalApiMetadataHost>
{
    private readonly PortalApiMetadataHost _host;

    public CareHomeOwnerPortalTests(PortalApiMetadataHost host) => _host = host;

    [Fact]
    public async Task FacilitiesMine_OwnedDraftFacility_Returns200WithExpectedIdsAndProfile()
    {
        DateTime now = UtcNow();
        User ownerA = CareHomeOwnerPortalFixture.CreateUser(AccountType.CareHomeOwner, 11);
        User ownerB = CareHomeOwnerPortalFixture.CreateUser(AccountType.CareHomeOwner, 12);
        await using CareHomesDbContext db = CareHomeOwnerPortalFixture.CreateDb();
        CareHomeFacility facilityA = CareHomeOwnerPortalFixture.SeedFacility(db, ownerA.Id, now, withProfile: true);
        CareHomeFacility facilityB = CareHomeOwnerPortalFixture.SeedFacility(db, ownerB.Id, now, withProfile: true);

        // Readiness readback before host startup.
        Assert.Equal(2, await db.Facilities.CountAsync());
        Assert.Equal(ownerA.Id, (await db.Facilities.SingleAsync(x => x.Id == facilityA.Id)).OwnerUserId);
        Assert.Equal(ownerB.Id, (await db.Facilities.SingleAsync(x => x.Id == facilityB.Id)).OwnerUserId);

        using var factory = CareHomeOwnerPortalFixture.OverrideCareHomes(_host, db);
        using HttpClient client = CareHomeOwnerPortalFixture.CreateClient(factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", CareHomeOwnerPortalFixture.CreateToken(_host, ownerA));

        using HttpResponseMessage response = await client.GetAsync("/api/v1/care-homes/facilities/mine");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(facilityA.Id.Value.ToString("D"), body.RootElement.GetProperty("id").GetString());
        Assert.Equal((int)facilityA.Status, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(facilityA.Version, body.RootElement.GetProperty("version").GetInt32());
        Assert.Equal("Hope House", body.RootElement.GetProperty("draft").GetProperty("englishName").GetString());
        Assert.Equal("Cairo", body.RootElement.GetProperty("draft").GetProperty("governorate").GetString());
        Assert.Equal(JsonValueKind.Array, body.RootElement.GetProperty("documents").ValueKind);
        Assert.NotEqual(
            facilityB.Id.Value.ToString("D"),
            body.RootElement.GetProperty("id").GetString());
    }

    [Fact]
    public async Task FacilitiesMine_NoFacility_Returns404WithStableCode()
    {
        DateTime now = UtcNow();
        User owner = CareHomeOwnerPortalFixture.CreateUser(AccountType.CareHomeOwner, 13);
        await using CareHomesDbContext db = CareHomeOwnerPortalFixture.CreateDb();

        Assert.Equal(0, await db.Facilities.CountAsync());

        using var factory = CareHomeOwnerPortalFixture.OverrideCareHomes(_host, db);
        using HttpClient client = CareHomeOwnerPortalFixture.CreateClient(factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", CareHomeOwnerPortalFixture.CreateToken(_host, owner));

        using HttpResponseMessage response = await client.GetAsync("/api/v1/care-homes/facilities/mine");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("CareHomes.Facility.NotFound", body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task InventoryMine_Populated_ReturnsNestedRoomBedAndIsolation()
    {
        DateTime now = UtcNow();
        User ownerA = CareHomeOwnerPortalFixture.CreateUser(AccountType.CareHomeOwner, 21);
        User ownerB = CareHomeOwnerPortalFixture.CreateUser(AccountType.CareHomeOwner, 22);
        await using CareHomesDbContext db = CareHomeOwnerPortalFixture.CreateDb();
        CareHomeFacility facilityA = CareHomeOwnerPortalFixture.SeedFacility(db, ownerA.Id, now, withProfile: false);
        CareHomeFacility facilityB = CareHomeOwnerPortalFixture.SeedFacility(db, ownerB.Id, now, withProfile: false);
        CareHomeOwnerPortalFixture.InventorySeed seedA =
            CareHomeOwnerPortalFixture.SeedInventory(db, facilityA.Id, now, "101", "A");
        CareHomeOwnerPortalFixture.InventorySeed seedB =
            CareHomeOwnerPortalFixture.SeedInventory(db, facilityB.Id, now, "201", "Z");

        Assert.Equal(2, await db.RoomTypes.CountAsync());
        Assert.Equal(facilityA.Id, (await db.Rooms.SingleAsync(x => x.Id == seedA.Room.Id)).FacilityId);

        using var factory = CareHomeOwnerPortalFixture.OverrideCareHomes(_host, db);
        using HttpClient client = CareHomeOwnerPortalFixture.CreateClient(factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", CareHomeOwnerPortalFixture.CreateToken(_host, ownerA));

        using HttpResponseMessage response = await client.GetAsync("/api/v1/care-homes/inventory/mine");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(facilityA.Id.Value.ToString("D"), body.RootElement.GetProperty("facilityId").GetString());
        JsonElement type = Assert.Single(body.RootElement.GetProperty("roomTypes").EnumerateArray());
        Assert.Equal(seedA.RoomType.Id.ToString("D"), type.GetProperty("id").GetString());
        Assert.Equal("Shared room", type.GetProperty("englishName").GetString());
        Assert.Equal(1000m, type.GetProperty("monthlyPriceEgp").GetDecimal());
        Assert.Equal((int)CareHomeAllocationMode.Shared, type.GetProperty("allocationMode").GetInt32());
        JsonElement room = Assert.Single(body.RootElement.GetProperty("rooms").EnumerateArray());
        Assert.Equal("101", room.GetProperty("roomNumber").GetString());
        Assert.Equal("Shared room", room.GetProperty("resolvedEnglishName").GetString());
        Assert.Null(room.GetProperty("arabicDisplayName").GetString());
        JsonElement bed = Assert.Single(body.RootElement.GetProperty("beds").EnumerateArray());
        Assert.Equal("A", bed.GetProperty("label").GetString());
        Assert.Equal(JsonValueKind.Array, body.RootElement.GetProperty("maintenance").ValueKind);
        Assert.NotEqual(seedB.RoomType.Id.ToString("D"), type.GetProperty("id").GetString());
        Assert.NotEqual(seedB.Room.Id.ToString("D"), room.GetProperty("id").GetString());
        Assert.NotEqual(seedB.Bed.Id.ToString("D"), bed.GetProperty("id").GetString());
    }

    [Fact]
    public async Task InventoryMine_NoFacility_Returns404()
    {
        User owner = CareHomeOwnerPortalFixture.CreateUser(AccountType.CareHomeOwner, 23);
        await using CareHomesDbContext db = CareHomeOwnerPortalFixture.CreateDb();
        using var factory = CareHomeOwnerPortalFixture.OverrideCareHomes(_host, db);
        using HttpClient client = CareHomeOwnerPortalFixture.CreateClient(factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", CareHomeOwnerPortalFixture.CreateToken(_host, owner));

        using HttpResponseMessage response = await client.GetAsync("/api/v1/care-homes/inventory/mine");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task BookingRequestsMine_PopulatedAndEmpty_WithIsolation()
    {
        DateTime now = UtcNow();
        User ownerA = CareHomeOwnerPortalFixture.CreateUser(AccountType.CareHomeOwner, 31);
        User ownerB = CareHomeOwnerPortalFixture.CreateUser(AccountType.CareHomeOwner, 32);
        User ownerEmpty = CareHomeOwnerPortalFixture.CreateUser(AccountType.CareHomeOwner, 33);
        await using CareHomesDbContext db = CareHomeOwnerPortalFixture.CreateDb();
        CareHomeFacility facilityA = CareHomeOwnerPortalFixture.SeedFacility(db, ownerA.Id, now, withProfile: false);
        CareHomeFacility facilityB = CareHomeOwnerPortalFixture.SeedFacility(db, ownerB.Id, now, withProfile: false);
        CareHomeOwnerPortalFixture.SeedFacility(db, ownerEmpty.Id, now, withProfile: false);
        CareHomeOwnerPortalFixture.InventorySeed seedA =
            CareHomeOwnerPortalFixture.SeedInventory(db, facilityA.Id, now);
        CareHomeOwnerPortalFixture.InventorySeed seedB =
            CareHomeOwnerPortalFixture.SeedInventory(db, facilityB.Id, now, "202", "B");
        CareHomeBooking bookingA = CareHomeOwnerPortalFixture.SeedPaidBooking(
            db, facilityA.Id, seedA.RoomType.Id, now, transactionId: 501);
        CareHomeBooking bookingB = CareHomeOwnerPortalFixture.SeedPaidBooking(
            db, facilityB.Id, seedB.RoomType.Id, now, transactionId: 502);
        CareHomeOwnerPortalFixture.SeedPendingBooking(db, facilityA.Id, seedA.RoomType.Id, now);

        // Only paid/failed-excluded rows are listed: the pending hold stays out.
        Assert.Equal(3, await db.Bookings.CountAsync());

        using var factory = CareHomeOwnerPortalFixture.OverrideCareHomes(_host, db);

        using HttpClient clientA = CareHomeOwnerPortalFixture.CreateClient(factory);
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", CareHomeOwnerPortalFixture.CreateToken(_host, ownerA));
        using HttpResponseMessage responseA = await clientA.GetAsync("/api/v1/care-homes/booking-requests/mine");
        Assert.Equal(HttpStatusCode.OK, responseA.StatusCode);
        using JsonDocument bodyA = JsonDocument.Parse(await responseA.Content.ReadAsStringAsync());
        JsonElement item = Assert.Single(bodyA.RootElement.EnumerateArray());
        Assert.Equal(bookingA.Id.ToString("D"), item.GetProperty("id").GetString());
        Assert.Equal(facilityA.Id.Value.ToString("D"), item.GetProperty("facilityId").GetString());
        Assert.Equal(seedA.RoomType.Id.ToString("D"), item.GetProperty("roomTypeId").GetString());
        Assert.Equal(1150m, item.GetProperty("totalAmount").GetDecimal());
        Assert.Equal("EGP", item.GetProperty("currency").GetString());
        Assert.Equal((int)CareHomeBookingStatus.Accepted, item.GetProperty("status").GetInt32());
        Assert.Equal((int)CareHomeBookingPaymentStatus.Paid, item.GetProperty("paymentStatus").GetInt32());
        Assert.DoesNotContain(
            bookingB.Id.ToString("D"),
            await responseA.Content.ReadAsStringAsync());

        using HttpClient clientEmpty = CareHomeOwnerPortalFixture.CreateClient(factory);
        clientEmpty.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", CareHomeOwnerPortalFixture.CreateToken(_host, ownerEmpty));
        using HttpResponseMessage responseEmpty = await clientEmpty.GetAsync("/api/v1/care-homes/booking-requests/mine");
        Assert.Equal(HttpStatusCode.OK, responseEmpty.StatusCode);
        using JsonDocument bodyEmpty = JsonDocument.Parse(await responseEmpty.Content.ReadAsStringAsync());
        Assert.Equal(0, bodyEmpty.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task BookingRequestsMine_NoFacility_Returns404()
    {
        User owner = CareHomeOwnerPortalFixture.CreateUser(AccountType.CareHomeOwner, 34);
        await using CareHomesDbContext db = CareHomeOwnerPortalFixture.CreateDb();
        using var factory = CareHomeOwnerPortalFixture.OverrideCareHomes(_host, db);
        using HttpClient client = CareHomeOwnerPortalFixture.CreateClient(factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", CareHomeOwnerPortalFixture.CreateToken(_host, owner));

        using HttpResponseMessage response = await client.GetAsync("/api/v1/care-homes/booking-requests/mine");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("CareHomes.Bookings.NotFound", body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task VisitsMine_PopulatedAndEmpty_WithIsolation()
    {
        DateTime now = UtcNow();
        User ownerA = CareHomeOwnerPortalFixture.CreateUser(AccountType.CareHomeOwner, 41);
        User ownerB = CareHomeOwnerPortalFixture.CreateUser(AccountType.CareHomeOwner, 42);
        User ownerEmpty = CareHomeOwnerPortalFixture.CreateUser(AccountType.CareHomeOwner, 43);
        await using CareHomesDbContext db = CareHomeOwnerPortalFixture.CreateDb();
        CareHomeFacility facilityA = CareHomeOwnerPortalFixture.SeedFacility(db, ownerA.Id, now, withProfile: false);
        CareHomeFacility facilityB = CareHomeOwnerPortalFixture.SeedFacility(db, ownerB.Id, now, withProfile: false);
        CareHomeOwnerPortalFixture.SeedFacility(db, ownerEmpty.Id, now, withProfile: false);
        DateTime startsAt = now.AddDays(4);
        CareHomeVisit visitA = CareHomeOwnerPortalFixture.SeedVisit(
            db, facilityA.Id, now, startsAt, "Portal Visitor A");
        CareHomeOwnerPortalFixture.SeedVisit(db, facilityB.Id, now, startsAt, "Foreign Visitor");

        Assert.Equal(2, await db.Visits.CountAsync());

        using var factory = CareHomeOwnerPortalFixture.OverrideCareHomes(_host, db);

        using HttpClient clientA = CareHomeOwnerPortalFixture.CreateClient(factory);
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", CareHomeOwnerPortalFixture.CreateToken(_host, ownerA));
        using HttpResponseMessage responseA = await clientA.GetAsync("/api/v1/care-homes/visits/mine");
        Assert.Equal(HttpStatusCode.OK, responseA.StatusCode);
        using JsonDocument bodyA = JsonDocument.Parse(await responseA.Content.ReadAsStringAsync());
        JsonElement row = Assert.Single(bodyA.RootElement.EnumerateArray());
        Assert.Equal(visitA.Id.ToString("D"), row.GetProperty("id").GetString());
        Assert.Equal(facilityA.Id.Value.ToString("D"), row.GetProperty("facilityId").GetString());
        Assert.Equal("Portal Visitor A", row.GetProperty("visitorName").GetString());
        Assert.Equal(2, row.GetProperty("visitorCount").GetInt32());
        Assert.Equal((int)CareHomeVisitKind.Prospective, row.GetProperty("kind").GetInt32());
        Assert.Equal((int)CareHomeVisitStatus.Pending, row.GetProperty("status").GetInt32());
        Assert.DoesNotContain("Foreign Visitor", await responseA.Content.ReadAsStringAsync());

        using HttpClient clientEmpty = CareHomeOwnerPortalFixture.CreateClient(factory);
        clientEmpty.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", CareHomeOwnerPortalFixture.CreateToken(_host, ownerEmpty));
        using HttpResponseMessage responseEmpty = await clientEmpty.GetAsync("/api/v1/care-homes/visits/mine");
        Assert.Equal(HttpStatusCode.OK, responseEmpty.StatusCode);
        using JsonDocument bodyEmpty = JsonDocument.Parse(await responseEmpty.Content.ReadAsStringAsync());
        Assert.Equal(0, bodyEmpty.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task DashboardMine_Populated_AssertsCountsAndRevenueTotals()
    {
        DateTime now = UtcNow();
        User owner = CareHomeOwnerPortalFixture.CreateUser(AccountType.CareHomeOwner, 51);
        await using CareHomesDbContext db = CareHomeOwnerPortalFixture.CreateDb();
        CareHomeFacility facility = CareHomeOwnerPortalFixture.SeedFacility(db, owner.Id, now, withProfile: false);
        CareHomeRoomType shared = CareHomeRoomType.Create(
            facility.Id, "غرفة مشتركة", "Shared room", 1000m, CareHomeAllocationMode.Shared, now);
        CareHomeRoom sharedRoom = CareHomeRoom.Create(facility.Id, shared.Id, "S1", now);
        CareHomeBed bedA = CareHomeBed.Create(facility.Id, sharedRoom.Id, "A", now);
        CareHomeBed bedB = CareHomeBed.Create(facility.Id, sharedRoom.Id, "B", now);
        db.RoomTypes.Add(shared);
        db.Rooms.Add(sharedRoom);
        db.Beds.AddRange(bedA, bedB);
        CareHomeOwnerPortalFixture.SeedPendingBooking(db, facility.Id, shared.Id, now);
        CareHomeOwnerPortalFixture.SeedPaidBooking(db, facility.Id, shared.Id, now, transactionId: 601, accept: false);
        CareHomeOwnerPortalFixture.SeedPaidBooking(db, facility.Id, shared.Id, now, transactionId: 602, accept: true);

        Assert.Equal(3, await db.Bookings.CountAsync());

        using var factory = CareHomeOwnerPortalFixture.OverrideCareHomes(_host, db);
        using HttpClient client = CareHomeOwnerPortalFixture.CreateClient(factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", CareHomeOwnerPortalFixture.CreateToken(_host, owner));

        using HttpResponseMessage response = await client.GetAsync("/api/v1/care-homes/dashboard/mine");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(2, body.RootElement.GetProperty("totalResources").GetInt32());
        Assert.Equal(1, body.RootElement.GetProperty("pendingPaymentCount").GetInt32());
        Assert.Equal(1, body.RootElement.GetProperty("awaitingDecisionCount").GetInt32());
        JsonElement revenue = body.RootElement.GetProperty("periodRevenue");
        Assert.Equal(2300m, revenue.GetProperty("grossCollected").GetDecimal());
        Assert.Equal(2, revenue.GetProperty("paymentCount").GetInt32());
    }

    [Fact]
    public async Task RevenueMine_Populated_AssertsTotalsDateFiltersAndInvalidRange()
    {
        DateTime now = UtcNow();
        User ownerA = CareHomeOwnerPortalFixture.CreateUser(AccountType.CareHomeOwner, 61);
        User ownerB = CareHomeOwnerPortalFixture.CreateUser(AccountType.CareHomeOwner, 62);
        await using CareHomesDbContext db = CareHomeOwnerPortalFixture.CreateDb();
        CareHomeFacility facilityA = CareHomeOwnerPortalFixture.SeedFacility(db, ownerA.Id, now, withProfile: false);
        CareHomeFacility facilityB = CareHomeOwnerPortalFixture.SeedFacility(db, ownerB.Id, now, withProfile: false);
        CareHomeOwnerPortalFixture.InventorySeed seedA =
            CareHomeOwnerPortalFixture.SeedInventory(db, facilityA.Id, now);
        CareHomeOwnerPortalFixture.InventorySeed seedB =
            CareHomeOwnerPortalFixture.SeedInventory(db, facilityB.Id, now, "202", "B");
        CareHomeBooking bookingA = CareHomeOwnerPortalFixture.SeedPaidBooking(
            db, facilityA.Id, seedA.RoomType.Id, now, transactionId: 701);
        CareHomeOwnerPortalFixture.SeedPaidBooking(db, facilityB.Id, seedB.RoomType.Id, now, transactionId: 702);

        DateOnly paymentDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
            now, Sanad.Modules.CareHomes.Application.Bookings.CareHomeRevenueProjection.CairoZone()));
        string day = paymentDay.ToString("yyyy-MM-dd");
        string future = paymentDay.AddDays(30).ToString("yyyy-MM-dd");

        using var factory = CareHomeOwnerPortalFixture.OverrideCareHomes(_host, db);
        using HttpClient client = CareHomeOwnerPortalFixture.CreateClient(factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", CareHomeOwnerPortalFixture.CreateToken(_host, ownerA));

        using HttpResponseMessage all = await client.GetAsync("/api/v1/care-homes/revenue/mine");
        Assert.Equal(HttpStatusCode.OK, all.StatusCode);
        using JsonDocument allBody = JsonDocument.Parse(await all.Content.ReadAsStringAsync());
        Assert.Equal(1150m, allBody.RootElement.GetProperty("totals").GetProperty("netCollected").GetDecimal());
        Assert.Equal(1, allBody.RootElement.GetProperty("totals").GetProperty("paymentCount").GetInt32());
        JsonElement tx = Assert.Single(allBody.RootElement.GetProperty("transactions").EnumerateArray());
        Assert.Equal(bookingA.Id.ToString("D"), tx.GetProperty("bookingId").GetString());
        Assert.Equal("EGP", tx.GetProperty("currency").GetString());
        Assert.Equal(1150m, tx.GetProperty("netMovement").GetDecimal());

        using HttpResponseMessage inRange = await client.GetAsync(
            $"/api/v1/care-homes/revenue/mine?from={day}&to={day}");
        Assert.Equal(HttpStatusCode.OK, inRange.StatusCode);
        using JsonDocument inRangeBody = JsonDocument.Parse(await inRange.Content.ReadAsStringAsync());
        Assert.Equal(1, inRangeBody.RootElement.GetProperty("transactions").GetArrayLength());

        using HttpResponseMessage outOfRange = await client.GetAsync(
            $"/api/v1/care-homes/revenue/mine?from={future}&to={future}");
        Assert.Equal(HttpStatusCode.OK, outOfRange.StatusCode);
        using JsonDocument outBody = JsonDocument.Parse(await outOfRange.Content.ReadAsStringAsync());
        Assert.Equal(0, outBody.RootElement.GetProperty("transactions").GetArrayLength());
        Assert.Equal(0m, outBody.RootElement.GetProperty("totals").GetProperty("grossCollected").GetDecimal());

        using HttpResponseMessage reversed = await client.GetAsync(
            $"/api/v1/care-homes/revenue/mine?from={future}&to={day}");
        Assert.Equal(HttpStatusCode.BadRequest, reversed.StatusCode);
        using JsonDocument reversedBody = JsonDocument.Parse(await reversed.Content.ReadAsStringAsync());
        Assert.Equal("CareHomes.Revenue.InvalidRange", reversedBody.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task RevenueMine_EmptyDraftFacility_ReturnsZeroTotals()
    {
        DateTime now = UtcNow();
        User owner = CareHomeOwnerPortalFixture.CreateUser(AccountType.CareHomeOwner, 63);
        await using CareHomesDbContext db = CareHomeOwnerPortalFixture.CreateDb();
        CareHomeOwnerPortalFixture.SeedFacility(db, owner.Id, now, withProfile: false);
        using var factory = CareHomeOwnerPortalFixture.OverrideCareHomes(_host, db);
        using HttpClient client = CareHomeOwnerPortalFixture.CreateClient(factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", CareHomeOwnerPortalFixture.CreateToken(_host, owner));

        using HttpResponseMessage dashboard = await client.GetAsync("/api/v1/care-homes/dashboard/mine");
        Assert.Equal(HttpStatusCode.OK, dashboard.StatusCode);
        using JsonDocument dashboardBody = JsonDocument.Parse(await dashboard.Content.ReadAsStringAsync());
        Assert.Equal(0, dashboardBody.RootElement.GetProperty("totalResources").GetInt32());
        Assert.Equal(0m, dashboardBody.RootElement.GetProperty("occupancyPercentage").GetDecimal());

        using HttpResponseMessage revenue = await client.GetAsync("/api/v1/care-homes/revenue/mine");
        Assert.Equal(HttpStatusCode.OK, revenue.StatusCode);
        using JsonDocument revenueBody = JsonDocument.Parse(await revenue.Content.ReadAsStringAsync());
        Assert.Equal(0m, revenueBody.RootElement.GetProperty("totals").GetProperty("netCollected").GetDecimal());
        Assert.Equal(0, revenueBody.RootElement.GetProperty("transactions").GetArrayLength());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VisitSettings_Lifecycle_CreatesUpdatesConflictsAndValidates_WithIsolation(bool approved)
    {
        DateTime now = UtcNow();
        User ownerA = CareHomeOwnerPortalFixture.CreateUser(AccountType.CareHomeOwner, 71);
        User ownerB = CareHomeOwnerPortalFixture.CreateUser(AccountType.CareHomeOwner, 72);
        await using CareHomesDbContext db = CareHomeOwnerPortalFixture.CreateDb();
        CareHomeFacility facilityA = CareHomeOwnerPortalFixture.SeedFacility(
            db, ownerA.Id, now, withProfile: false, approved: approved);
        CareHomeFacility facilityB = CareHomeOwnerPortalFixture.SeedFacility(db, ownerB.Id, now, withProfile: false);

        Assert.Equal(0, await db.VisitSettings.CountAsync());
        Assert.Equal(approved ? CareHomeStatus.Approved : CareHomeStatus.Draft,
            (await db.Facilities.AsNoTracking().SingleAsync(x => x.Id == facilityA.Id)).Status);

        using var factory = CareHomeOwnerPortalFixture.OverrideCareHomes(_host, db);
        using HttpClient clientA = CareHomeOwnerPortalFixture.CreateClient(factory);
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", CareHomeOwnerPortalFixture.CreateToken(_host, ownerA));

        using HttpResponseMessage initial = await clientA.GetAsync("/api/v1/care-homes/facilities/mine/visit-settings");
        Assert.Equal(HttpStatusCode.OK, initial.StatusCode);
        using JsonDocument initialBody = JsonDocument.Parse(await initial.Content.ReadAsStringAsync());
        Assert.Equal(facilityA.Id.Value.ToString("D"), initialBody.RootElement.GetProperty("facilityId").GetString());
        Assert.Equal(0, initialBody.RootElement.GetProperty("version").GetInt32());

        object create = new
        {
            expectedVersion = 0,
            visitorCapacity = 4,
            operatingHours = new[] { new { day = 1, startTime = "09:00", endTime = "17:00" } },
            visitingWindows = new[] { new { day = 1, startTime = "10:00", endTime = "16:00" } },
            closures = Array.Empty<object>()
        };
        using HttpResponseMessage created = await clientA.PutAsJsonAsync(
            "/api/v1/care-homes/facilities/mine/visit-settings", create);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        using JsonDocument createdBody = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        Assert.Equal(1, createdBody.RootElement.GetProperty("version").GetInt32());
        Assert.Equal(4, createdBody.RootElement.GetProperty("visitorCapacity").GetInt32());

        using HttpResponseMessage readback = await clientA.GetAsync("/api/v1/care-homes/facilities/mine/visit-settings");
        using JsonDocument readbackBody = JsonDocument.Parse(await readback.Content.ReadAsStringAsync());
        Assert.Equal(1, readbackBody.RootElement.GetProperty("version").GetInt32());

        object stale = new
        {
            expectedVersion = 0,
            visitorCapacity = 8,
            operatingHours = new[] { new { day = 1, startTime = "09:00", endTime = "17:00" } },
            visitingWindows = new[] { new { day = 1, startTime = "10:00", endTime = "16:00" } },
            closures = Array.Empty<object>()
        };
        using HttpResponseMessage conflict = await clientA.PutAsJsonAsync(
            "/api/v1/care-homes/facilities/mine/visit-settings", stale);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        using JsonDocument conflictBody = JsonDocument.Parse(await conflict.Content.ReadAsStringAsync());
        Assert.Equal("CareHomes.Visits.Conflict", conflictBody.RootElement.GetProperty("code").GetString());

        using HttpResponseMessage afterConflict = await clientA.GetAsync("/api/v1/care-homes/facilities/mine/visit-settings");
        using JsonDocument afterConflictBody = JsonDocument.Parse(await afterConflict.Content.ReadAsStringAsync());
        Assert.Equal(1, afterConflictBody.RootElement.GetProperty("version").GetInt32());
        Assert.Equal(4, afterConflictBody.RootElement.GetProperty("visitorCapacity").GetInt32());

        using HttpClient clientB = CareHomeOwnerPortalFixture.CreateClient(factory);
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", CareHomeOwnerPortalFixture.CreateToken(_host, ownerB));
        using HttpResponseMessage foreign = await clientB.GetAsync("/api/v1/care-homes/facilities/mine/visit-settings");
        Assert.Equal(HttpStatusCode.OK, foreign.StatusCode);
        using JsonDocument foreignBody = JsonDocument.Parse(await foreign.Content.ReadAsStringAsync());
        Assert.Equal(facilityB.Id.Value.ToString("D"), foreignBody.RootElement.GetProperty("facilityId").GetString());
        Assert.Equal(0, foreignBody.RootElement.GetProperty("version").GetInt32());

        object update = new
        {
            expectedVersion = 1,
            visitorCapacity = 6,
            operatingHours = new[] { new { day = 1, startTime = "09:00", endTime = "17:00" } },
            visitingWindows = new[] { new { day = 1, startTime = "10:00", endTime = "16:00" } },
            closures = Array.Empty<object>()
        };
        using HttpResponseMessage updated = await clientA.PutAsJsonAsync(
            "/api/v1/care-homes/facilities/mine/visit-settings", update);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        using JsonDocument updatedBody = JsonDocument.Parse(await updated.Content.ReadAsStringAsync());
        Assert.Equal(2, updatedBody.RootElement.GetProperty("version").GetInt32());

        object invalidCapacity = new
        {
            expectedVersion = 2,
            visitorCapacity = 0,
            operatingHours = new[] { new { day = 1, startTime = "09:00", endTime = "17:00" } },
            visitingWindows = new[] { new { day = 1, startTime = "10:00", endTime = "16:00" } },
            closures = Array.Empty<object>()
        };
        using HttpResponseMessage invalid = await clientA.PutAsJsonAsync(
            "/api/v1/care-homes/facilities/mine/visit-settings", invalidCapacity);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using JsonDocument invalidBody = JsonDocument.Parse(await invalid.Content.ReadAsStringAsync());
        Assert.Equal("CareHomes.Visits.InvalidRequest", invalidBody.RootElement.GetProperty("code").GetString());

        using HttpResponseMessage afterInvalid = await clientA.GetAsync("/api/v1/care-homes/facilities/mine/visit-settings");
        using JsonDocument afterInvalidBody = JsonDocument.Parse(await afterInvalid.Content.ReadAsStringAsync());
        Assert.Equal(2, afterInvalidBody.RootElement.GetProperty("version").GetInt32());
        Assert.Equal(6, afterInvalidBody.RootElement.GetProperty("visitorCapacity").GetInt32());
    }

    [Fact]
    public async Task VisitSettings_StringEnumDay_IsRejectedWithoutChangingVersion()
    {
        DateTime now = UtcNow();
        User owner = CareHomeOwnerPortalFixture.CreateUser(AccountType.CareHomeOwner, 74);
        await using CareHomesDbContext db = CareHomeOwnerPortalFixture.CreateDb();
        CareHomeOwnerPortalFixture.SeedFacility(db, owner.Id, now, withProfile: false);
        using var factory = CareHomeOwnerPortalFixture.OverrideCareHomes(_host, db);
        using HttpClient client = CareHomeOwnerPortalFixture.CreateClient(factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", CareHomeOwnerPortalFixture.CreateToken(_host, owner));

        using StringContent content = new(
            "{\"expectedVersion\":0,\"visitorCapacity\":4,"
            + "\"operatingHours\":[{\"day\":\"Monday\",\"startTime\":\"09:00\",\"endTime\":\"17:00\"}],"
            + "\"visitingWindows\":[{\"day\":\"Monday\",\"startTime\":\"10:00\",\"endTime\":\"16:00\"}],"
            + "\"closures\":[]}",
            System.Text.Encoding.UTF8,
            "application/json");
        using HttpResponseMessage response = await client.PutAsync(
            "/api/v1/care-homes/facilities/mine/visit-settings", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using JsonDocument errorBody = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(
            errorBody.RootElement.GetProperty("errors").EnumerateObject()
                .Any(p => p.Name.Contains("operatingHours", StringComparison.Ordinal)),
            "String enum day names are rejected at model binding; numeric DayOfWeek values are the verified wire format.");

        using HttpResponseMessage readback = await client.GetAsync("/api/v1/care-homes/facilities/mine/visit-settings");
        using JsonDocument body = JsonDocument.Parse(await readback.Content.ReadAsStringAsync());
        Assert.Equal(0, body.RootElement.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task VisitSettings_NoFacility_Returns404()
    {
        User owner = CareHomeOwnerPortalFixture.CreateUser(AccountType.CareHomeOwner, 73);
        await using CareHomesDbContext db = CareHomeOwnerPortalFixture.CreateDb();
        using var factory = CareHomeOwnerPortalFixture.OverrideCareHomes(_host, db);
        using HttpClient client = CareHomeOwnerPortalFixture.CreateClient(factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", CareHomeOwnerPortalFixture.CreateToken(_host, owner));

        using HttpResponseMessage response = await client.GetAsync("/api/v1/care-homes/facilities/mine/visit-settings");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task OwnerRoutes_Anonymous_Returns401_And_NonOwner_Returns403()
    {
        DateTime now = UtcNow();
        User owner = CareHomeOwnerPortalFixture.CreateUser(AccountType.CareHomeOwner, 81);
        User admin = CareHomeOwnerPortalFixture.CreateUser(AccountType.SuperAdmin, 82);
        User family = CareHomeOwnerPortalFixture.CreateUser(AccountType.Family, 83);
        await using CareHomesDbContext db = CareHomeOwnerPortalFixture.CreateDb();
        CareHomeOwnerPortalFixture.SeedFacility(db, owner.Id, now, withProfile: false);

        string[] getRoutes =
        [
            "/api/v1/care-homes/facilities/mine",
            "/api/v1/care-homes/inventory/mine",
            "/api/v1/care-homes/booking-requests/mine",
            "/api/v1/care-homes/visits/mine",
            "/api/v1/care-homes/dashboard/mine",
            "/api/v1/care-homes/revenue/mine",
            "/api/v1/care-homes/facilities/mine/visit-settings"
        ];

        string adminToken = CareHomeOwnerPortalFixture.CreateToken(_host, admin);
        string familyToken = CareHomeOwnerPortalFixture.CreateToken(_host, family);
        using var factory = CareHomeOwnerPortalFixture.OverrideCareHomes(_host, db);

        foreach (string route in getRoutes)
        {
            using HttpClient anonymous = CareHomeOwnerPortalFixture.CreateClient(factory);
            using HttpResponseMessage denied = await anonymous.GetAsync(route);
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);

            using HttpClient asAdmin = CareHomeOwnerPortalFixture.CreateClient(factory);
            asAdmin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            using HttpResponseMessage adminForbidden = await asAdmin.GetAsync(route);
            Assert.Equal(HttpStatusCode.Forbidden, adminForbidden.StatusCode);

            using HttpClient asFamily = CareHomeOwnerPortalFixture.CreateClient(factory);
            asFamily.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", familyToken);
            using HttpResponseMessage familyForbidden = await asFamily.GetAsync(route);
            Assert.Equal(HttpStatusCode.Forbidden, familyForbidden.StatusCode);
        }

        object settingsBody = new
        {
            expectedVersion = 0,
            visitorCapacity = 4,
            operatingHours = new[] { new { day = 1, startTime = "09:00", endTime = "17:00" } },
            visitingWindows = new[] { new { day = 1, startTime = "10:00", endTime = "16:00" } },
            closures = Array.Empty<object>()
        };

        using HttpClient anonymousPut = CareHomeOwnerPortalFixture.CreateClient(factory);
        using HttpResponseMessage anonymousPutResponse = await anonymousPut.PutAsJsonAsync(
            "/api/v1/care-homes/facilities/mine/visit-settings", settingsBody);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousPutResponse.StatusCode);

        using HttpClient adminPut = CareHomeOwnerPortalFixture.CreateClient(factory);
        adminPut.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using HttpResponseMessage adminPutResponse = await adminPut.PutAsJsonAsync(
            "/api/v1/care-homes/facilities/mine/visit-settings", settingsBody);
        Assert.Equal(HttpStatusCode.Forbidden, adminPutResponse.StatusCode);

        using HttpClient familyPut = CareHomeOwnerPortalFixture.CreateClient(factory);
        familyPut.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", familyToken);
        using HttpResponseMessage familyPutResponse = await familyPut.PutAsJsonAsync(
            "/api/v1/care-homes/facilities/mine/visit-settings", settingsBody);
        Assert.Equal(HttpStatusCode.Forbidden, familyPutResponse.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FacilityState_AllOwnerReads_ReturnExpectedOwnedData(bool approved)
    {
        DateTime now = UtcNow();
        User owner = CareHomeOwnerPortalFixture.CreateUser(AccountType.CareHomeOwner, 91);
        await using CareHomesDbContext db = CareHomeOwnerPortalFixture.CreateDb();
        CareHomeFacility facility = CareHomeOwnerPortalFixture.SeedFacility(
            db, owner.Id, now, withProfile: true, approved: approved);
        CareHomeOwnerPortalFixture.InventorySeed seed =
            CareHomeOwnerPortalFixture.SeedInventory(db, facility.Id, now);
        CareHomeBooking booking = CareHomeOwnerPortalFixture.SeedPaidBooking(
            db, facility.Id, seed.RoomType.Id, now, transactionId: 901);
        var visit = CareHomeOwnerPortalFixture.SeedVisit(db, facility.Id, now, now.AddDays(5));

        Assert.Equal(approved ? CareHomeStatus.Approved : CareHomeStatus.Draft,
            (await db.Facilities.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(facility.Id, (await db.Rooms.AsNoTracking().SingleAsync()).FacilityId);
        Assert.Equal(booking.Id, (await db.Bookings.AsNoTracking().SingleAsync()).Id);
        Assert.Equal(visit.Id, (await db.Visits.AsNoTracking().SingleAsync()).Id);

        using var factory = CareHomeOwnerPortalFixture.OverrideCareHomes(_host, db);
        using HttpClient client = CareHomeOwnerPortalFixture.CreateClient(factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", CareHomeOwnerPortalFixture.CreateToken(_host, owner));

        string[] routes =
        [
            "/api/v1/care-homes/facilities/mine",
            "/api/v1/care-homes/inventory/mine",
            "/api/v1/care-homes/booking-requests/mine",
            "/api/v1/care-homes/visits/mine",
            "/api/v1/care-homes/dashboard/mine",
            "/api/v1/care-homes/revenue/mine",
            "/api/v1/care-homes/facilities/mine/visit-settings"
        ];

        foreach (string route in routes)
        {
            using HttpResponseMessage response = await client.GetAsync(route);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using JsonDocument payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            JsonElement value = payload.RootElement;
            switch (route)
            {
                case "/api/v1/care-homes/facilities/mine":
                    Assert.Equal(facility.Id.Value.ToString("D"), value.GetProperty("id").GetString());
                    Assert.Equal((int)facility.Status, value.GetProperty("status").GetInt32());
                    break;
                case "/api/v1/care-homes/inventory/mine":
                    Assert.Equal(seed.RoomType.Id.ToString("D"),
                        Assert.Single(value.GetProperty("roomTypes").EnumerateArray()).GetProperty("id").GetString());
                    Assert.Single(value.GetProperty("beds").EnumerateArray());
                    break;
                case "/api/v1/care-homes/booking-requests/mine":
                    Assert.Equal(booking.Id.ToString("D"), Assert.Single(value.EnumerateArray()).GetProperty("id").GetString());
                    break;
                case "/api/v1/care-homes/visits/mine":
                    Assert.Equal(visit.Id.ToString("D"), Assert.Single(value.EnumerateArray()).GetProperty("id").GetString());
                    break;
                case "/api/v1/care-homes/dashboard/mine":
                    Assert.Equal(1, value.GetProperty("totalResources").GetInt32());
                    Assert.Equal(1150m, value.GetProperty("periodRevenue").GetProperty("grossCollected").GetDecimal());
                    break;
                case "/api/v1/care-homes/revenue/mine":
                    Assert.Equal(1150m, value.GetProperty("totals").GetProperty("netCollected").GetDecimal());
                    Assert.Single(value.GetProperty("transactions").EnumerateArray());
                    break;
                default:
                    Assert.Equal(facility.Id.Value.ToString("D"), value.GetProperty("facilityId").GetString());
                    Assert.Equal(0, value.GetProperty("version").GetInt32());
                    break;
            }
        }
    }

    [Theory]
    [InlineData("dashboard/mine")]
    [InlineData("revenue/mine")]
    public async Task FinanceReads_NoFacility_Return404(string route)
    {
        User owner = CareHomeOwnerPortalFixture.CreateUser(AccountType.CareHomeOwner, 92);
        await using CareHomesDbContext db = CareHomeOwnerPortalFixture.CreateDb();
        Assert.Equal(0, await db.Facilities.CountAsync());
        using var factory = CareHomeOwnerPortalFixture.OverrideCareHomes(_host, db);
        using HttpClient client = CareHomeOwnerPortalFixture.CreateClient(factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", CareHomeOwnerPortalFixture.CreateToken(_host, owner));
        using HttpResponseMessage response = await client.GetAsync($"/api/v1/care-homes/{route}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using JsonDocument payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("CareHomes.Bookings.NotFound", payload.RootElement.GetProperty("code").GetString());
    }

    private static DateTime UtcNow() =>
        DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
}
