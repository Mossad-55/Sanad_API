using Microsoft.EntityFrameworkCore;
using Npgsql;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.CareHomes.Application.Bookings;
using Ahlena.Modules.CareHomes.Application.Inventory;
using Ahlena.Modules.CareHomes.Domain.Bookings;
using Ahlena.Modules.CareHomes.Domain.Facilities;
using Ahlena.Modules.CareHomes.Infrastructure.Bookings;
using Ahlena.Modules.CareHomes.Infrastructure.Persistence;
using Xunit;

namespace Ahlena.UnitTests.CareHomes;

/// <summary>
/// FUR-03 relational proof against a validated disposable PostgreSQL target.
/// Executes the exact production finance query handlers (admin filtered/unfiltered,
/// owner Draft/Approved) through Npgsql on PostgreSQL. Never resets, drops,
/// truncates, or broadly seeds the database. Only inserts minimal fixture rows
/// and deletes them by ID in a finally block. The target connection string must
/// be supplied as ConnectionStrings__Fur03VerificationDatabase in the explicit
/// test child process only; the guarded suite leaves it absent and these tests
/// skip. Endpoint and database name are validated before any write; anything
/// else fails closed without touching a database.
/// </summary>
[Collection("Fur03Verification")]
public sealed class CareHomeFinancePostgresTests
{
    private const string ConnectionStringVariable = "ConnectionStrings__Fur03VerificationDatabase";

    private const string ExpectedDatabase = "AhlenaFur03VerifyDb";

    private static readonly DateTime FixedUtcNow = new(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);

    private static readonly DateOnly FixedToday = DateOnly.FromDateTime(
        TimeZoneInfo.ConvertTimeFromUtc(FixedUtcNow, CareHomeRevenueProjection.CairoZone()));

    private static readonly CareHomeProfileDraft ProfileDraft = new(
        "دارالأمل", "Hope House", "رعاية وإقامة آمنة", "Safe care and accommodation",
        "Contact name", "+201000000000", null, "Cairo", "Nasr City", "District 1",
        "Example street address", "الشروط", "Admission conditions", [], []);

    [Fur03PostgresFact]
    public async Task Admin_Revenue_Unfiltered_ReturnsAllFacilitiesData()
    {
        string connectionString = GetValidatedConnectionString();

        List<CareHomeId> facilityIdsToDelete = [];
        List<Guid> bookingIdsToDelete = [];

        try
        {
            await using var setupDb = CreateDbContext(connectionString);

            UserId ownerA = UserId.New();
            UserId ownerB = UserId.New();
            CareHomeFacility facilityA = CareHomeFacility.CreateDraft(ownerA, FixedUtcNow);
            CareHomeFacility facilityB = CareHomeFacility.CreateDraft(ownerB, FixedUtcNow);
            setupDb.Facilities.AddRange(facilityA, facilityB);
            await setupDb.SaveChangesAsync();

            facilityIdsToDelete.Add(facilityA.Id);
            facilityIdsToDelete.Add(facilityB.Id);

            Guid roomTypeId = Guid.NewGuid();
            CareHomeBooking bookingA1 = CreatePaidBooking(facilityA.Id, roomTypeId, 11, FixedUtcNow);
            CareHomeBooking bookingA2 = CreatePaidAndRefundedBooking(facilityA.Id, roomTypeId, 12,
                FixedUtcNow.AddDays(-1), FixedUtcNow);
            CareHomeBooking bookingB1 = CreatePaidBooking(facilityB.Id, roomTypeId, 13, FixedUtcNow);

            setupDb.Bookings.AddRange(bookingA1, bookingA2, bookingB1);
            await setupDb.SaveChangesAsync();

            bookingIdsToDelete.Add(bookingA1.Id);
            bookingIdsToDelete.Add(bookingA2.Id);
            bookingIdsToDelete.Add(bookingB1.Id);

            // Readback prerequisites before the scenario.
            Assert.Equal(3, await setupDb.Bookings.CountAsync(
                x => x.FacilityId == facilityA.Id || x.FacilityId == facilityB.Id));

            await using var readDb = CreateDbContext(connectionString);
            var handler = new GetAdminCareHomeRevenueHandler(readDb);

            var all = await handler.Handle(new(null, null, null), default);

            Assert.True(all.IsSuccess);
            Assert.True(all.Value.Totals.PaymentCount >= 3);
            Assert.True(all.Value.Totals.RefundCount >= 1);
            Assert.Contains(all.Value.Transactions, x => x.BookingId == bookingA1.Id);
            Assert.Contains(all.Value.Transactions, x => x.BookingId == bookingA2.Id);
            Assert.Contains(all.Value.Transactions, x => x.BookingId == bookingB1.Id);
        }
        finally
        {
            await using var cleanupDb = CreateDbContext(connectionString);
            if (bookingIdsToDelete.Count > 0)
            {
                await cleanupDb.Bookings.Where(x => bookingIdsToDelete.Contains(x.Id))
                    .ExecuteDeleteAsync();
            }
            if (facilityIdsToDelete.Count > 0)
            {
                await cleanupDb.Facilities.Where(x => facilityIdsToDelete.Contains(x.Id))
                    .ExecuteDeleteAsync();
            }
        }
    }

    [Fur03PostgresFact]
    public async Task Admin_Revenue_Filtered_ReturnsOnlySpecifiedFacility_ExcludesForeignFacility()
    {
        string connectionString = GetValidatedConnectionString();

        List<CareHomeId> facilityIdsToDelete = [];
        List<Guid> bookingIdsToDelete = [];

        try
        {
            await using var setupDb = CreateDbContext(connectionString);

            UserId ownerA = UserId.New();
            UserId ownerB = UserId.New();
            CareHomeFacility facilityA = CareHomeFacility.CreateDraft(ownerA, FixedUtcNow);
            CareHomeFacility facilityB = CareHomeFacility.CreateDraft(ownerB, FixedUtcNow);
            setupDb.Facilities.AddRange(facilityA, facilityB);
            await setupDb.SaveChangesAsync();

            facilityIdsToDelete.Add(facilityA.Id);
            facilityIdsToDelete.Add(facilityB.Id);

            Guid roomTypeId = Guid.NewGuid();
            CareHomeBooking bookingA1 = CreatePaidBooking(facilityA.Id, roomTypeId, 21, FixedUtcNow);
            CareHomeBooking bookingA2 = CreatePaidBooking(facilityA.Id, roomTypeId, 22,
                FixedUtcNow.AddDays(-1));
            CareHomeBooking bookingB1 = CreatePaidBooking(facilityB.Id, roomTypeId, 23, FixedUtcNow);

            setupDb.Bookings.AddRange(bookingA1, bookingA2, bookingB1);
            await setupDb.SaveChangesAsync();

            bookingIdsToDelete.Add(bookingA1.Id);
            bookingIdsToDelete.Add(bookingA2.Id);
            bookingIdsToDelete.Add(bookingB1.Id);

            // Readback prerequisites before the scenario.
            Assert.Equal(3, await setupDb.Bookings.CountAsync(
                x => x.FacilityId == facilityA.Id || x.FacilityId == facilityB.Id));

            await using var readDb = CreateDbContext(connectionString);
            var handler = new GetAdminCareHomeRevenueHandler(readDb);

            var filteredA = await handler.Handle(new(facilityA.Id.Value, null, null), default);
            var filteredB = await handler.Handle(new(facilityB.Id.Value, null, null), default);

            Assert.True(filteredA.IsSuccess);
            Assert.Equal(2, filteredA.Value.Totals.PaymentCount);
            Assert.Equal(0, filteredA.Value.Totals.RefundCount);
            Assert.Equal(2300m, filteredA.Value.Totals.GrossCollected);
            Assert.Equal(2300m, filteredA.Value.Totals.NetCollected);
            Assert.Equal(2, filteredA.Value.Transactions.Count);
            Assert.All(filteredA.Value.Transactions, x =>
                Assert.Equal(facilityA.Id.Value, x.FacilityId));

            Assert.True(filteredB.IsSuccess);
            Assert.Equal(1, filteredB.Value.Totals.PaymentCount);
            Assert.Single(filteredB.Value.Transactions);
            Assert.Equal(facilityB.Id.Value, filteredB.Value.Transactions[0].FacilityId);
        }
        finally
        {
            await using var cleanupDb = CreateDbContext(connectionString);
            if (bookingIdsToDelete.Count > 0)
            {
                await cleanupDb.Bookings.Where(x => bookingIdsToDelete.Contains(x.Id))
                    .ExecuteDeleteAsync();
            }
            if (facilityIdsToDelete.Count > 0)
            {
                await cleanupDb.Facilities.Where(x => facilityIdsToDelete.Contains(x.Id))
                    .ExecuteDeleteAsync();
            }
        }
    }

    [Fur03PostgresFact]
    public async Task Admin_Revenue_UnknownFacility_ReturnsEmptySuccess()
    {
        string connectionString = GetValidatedConnectionString();

        await using var db = CreateDbContext(connectionString);
        var handler = new GetAdminCareHomeRevenueHandler(db);

        var unknown = await handler.Handle(new(Guid.NewGuid(), null, null), default);

        Assert.True(unknown.IsSuccess);
        Assert.Equal(0m, unknown.Value.Totals.GrossCollected);
        Assert.Equal(0m, unknown.Value.Totals.NetCollected);
        Assert.Empty(unknown.Value.Transactions);
    }

    [Fur03PostgresFact]
    public async Task Admin_Dashboard_Filtered_DistinguishesFacilities_ResourcesAndRevenue()
    {
        string connectionString = GetValidatedConnectionString();

        List<CareHomeId> facilityIdsToDelete = [];
        List<Guid> bookingIdsToDelete = [];

        try
        {
            await using var setupDb = CreateDbContext(connectionString);

            UserId ownerA = UserId.New();
            UserId ownerB = UserId.New();
            CareHomeFacility facilityA = CareHomeFacility.CreateDraft(ownerA, FixedUtcNow);
            CareHomeFacility facilityB = CareHomeFacility.CreateDraft(ownerB, FixedUtcNow);
            setupDb.Facilities.AddRange(facilityA, facilityB);
            await setupDb.SaveChangesAsync();

            facilityIdsToDelete.Add(facilityA.Id);
            facilityIdsToDelete.Add(facilityB.Id);

            // Facility A: shared room type, one room, two beds (2 shared-bed resources).
            CareHomeRoomType sharedTypeA = CareHomeRoomType.Create(
                facilityA.Id, "مشترك A", "Shared A", 1000m, CareHomeAllocationMode.Shared, FixedUtcNow);
            CareHomeRoom roomA = CareHomeRoom.Create(facilityA.Id, sharedTypeA.Id, "RA1", FixedUtcNow);
            CareHomeBed bedA1 = CareHomeBed.Create(facilityA.Id, roomA.Id, "A1", FixedUtcNow);
            CareHomeBed bedA2 = CareHomeBed.Create(facilityA.Id, roomA.Id, "A2", FixedUtcNow);
            setupDb.RoomTypes.Add(sharedTypeA);
            setupDb.Rooms.Add(roomA);
            setupDb.Beds.AddRange(bedA1, bedA2);

            // Facility B: private room type, one room, no beds (1 physical-room resource).
            CareHomeRoomType privateTypeB = CareHomeRoomType.Create(
                facilityB.Id, "خاص B", "Private B", 2000m, CareHomeAllocationMode.Private, FixedUtcNow);
            CareHomeRoom roomB = CareHomeRoom.Create(facilityB.Id, privateTypeB.Id, "RB1", FixedUtcNow);
            setupDb.RoomTypes.Add(privateTypeB);
            setupDb.Rooms.Add(roomB);

            // One paid booking per facility for revenue.
            CareHomeBooking bookingA = CreatePaidBooking(facilityA.Id, sharedTypeA.Id, 31, FixedUtcNow);
            CareHomeBooking bookingB = CreatePaidBooking(facilityB.Id, privateTypeB.Id, 32, FixedUtcNow);
            setupDb.Bookings.AddRange(bookingA, bookingB);
            await setupDb.SaveChangesAsync();

            bookingIdsToDelete.Add(bookingA.Id);
            bookingIdsToDelete.Add(bookingB.Id);

            // Readback prerequisites before the scenario.
            Assert.Equal(2, await setupDb.Rooms.CountAsync());
            Assert.Equal(2, await setupDb.Beds.CountAsync());
            Assert.Equal(2, await setupDb.Bookings.CountAsync());

            await using var readDb = CreateDbContext(connectionString);

            // Facility A: two shared-bed resources, one bed actively occupied today.
            var occupancyA = new FixedOccupancyProvider(
            [
                new(bedA1.Id, CareHomeResourceKind.Bed, FixedToday, FixedToday.AddDays(1))
            ]);
            var dashboardA = await new GetAdminCareHomeDashboardHandler(readDb, occupancyA)
                .Handle(new(facilityA.Id.Value, null, null, FixedUtcNow), default);

            // Facility B: one physical-room resource, no active occupancy.
            var occupancyB = new FixedOccupancyProvider([]);
            var dashboardB = await new GetAdminCareHomeDashboardHandler(readDb, occupancyB)
                .Handle(new(facilityB.Id.Value, null, null, FixedUtcNow), default);

            Assert.True(dashboardA.IsSuccess);
            Assert.Equal(2, dashboardA.Value.TotalResources);
            Assert.Equal(1, dashboardA.Value.OccupiedResources);
            Assert.Equal(1150m, dashboardA.Value.PeriodRevenue.GrossCollected);

            Assert.True(dashboardB.IsSuccess);
            Assert.Equal(1, dashboardB.Value.TotalResources);
            Assert.Equal(0, dashboardB.Value.OccupiedResources);
            Assert.Equal(1150m, dashboardB.Value.PeriodRevenue.GrossCollected);
        }
        finally
        {
            await using var cleanupDb = CreateDbContext(connectionString);
            if (bookingIdsToDelete.Count > 0)
            {
                await cleanupDb.Bookings.Where(x => bookingIdsToDelete.Contains(x.Id))
                    .ExecuteDeleteAsync();
            }
            if (facilityIdsToDelete.Count > 0)
            {
                await cleanupDb.Facilities.Where(x => facilityIdsToDelete.Contains(x.Id))
                    .ExecuteDeleteAsync();
            }
        }
    }

    [Fur03PostgresFact]
    public async Task Admin_Dashboard_Unfiltered_ReturnsAllFacilities()
    {
        string connectionString = GetValidatedConnectionString();

        List<CareHomeId> facilityIdsToDelete = [];
        List<Guid> bookingIdsToDelete = [];

        try
        {
            await using var setupDb = CreateDbContext(connectionString);

            UserId ownerA = UserId.New();
            UserId ownerB = UserId.New();
            CareHomeFacility facilityA = CareHomeFacility.CreateDraft(ownerA, FixedUtcNow);
            CareHomeFacility facilityB = CareHomeFacility.CreateDraft(ownerB, FixedUtcNow);
            setupDb.Facilities.AddRange(facilityA, facilityB);
            await setupDb.SaveChangesAsync();

            facilityIdsToDelete.Add(facilityA.Id);
            facilityIdsToDelete.Add(facilityB.Id);

            CareHomeRoomType sharedTypeA = CareHomeRoomType.Create(
                facilityA.Id, "مشترك U", "Shared U", 1000m, CareHomeAllocationMode.Shared, FixedUtcNow);
            CareHomeRoom roomA = CareHomeRoom.Create(facilityA.Id, sharedTypeA.Id, "RU1", FixedUtcNow);
            CareHomeBed bedA1 = CareHomeBed.Create(facilityA.Id, roomA.Id, "UA1", FixedUtcNow);
            CareHomeBed bedA2 = CareHomeBed.Create(facilityA.Id, roomA.Id, "UA2", FixedUtcNow);
            setupDb.RoomTypes.Add(sharedTypeA);
            setupDb.Rooms.Add(roomA);
            setupDb.Beds.AddRange(bedA1, bedA2);

            CareHomeRoomType privateTypeB = CareHomeRoomType.Create(
                facilityB.Id, "خاص U", "Private U", 2000m, CareHomeAllocationMode.Private, FixedUtcNow);
            CareHomeRoom roomB = CareHomeRoom.Create(facilityB.Id, privateTypeB.Id, "RU2", FixedUtcNow);
            setupDb.RoomTypes.Add(privateTypeB);
            setupDb.Rooms.Add(roomB);

            CareHomeBooking bookingA = CreatePaidBooking(facilityA.Id, sharedTypeA.Id, 41, FixedUtcNow);
            CareHomeBooking bookingB = CreatePaidBooking(facilityB.Id, privateTypeB.Id, 42, FixedUtcNow);
            setupDb.Bookings.AddRange(bookingA, bookingB);
            await setupDb.SaveChangesAsync();

            bookingIdsToDelete.Add(bookingA.Id);
            bookingIdsToDelete.Add(bookingB.Id);

            // Readback prerequisites before the scenario.
            Assert.Equal(2, await setupDb.Rooms.CountAsync());
            Assert.Equal(2, await setupDb.Beds.CountAsync());
            Assert.Equal(2, await setupDb.Bookings.CountAsync());

            await using var readDb = CreateDbContext(connectionString);
            var occupancy = new FixedOccupancyProvider(
            [
                new(bedA1.Id, CareHomeResourceKind.Bed, FixedToday, FixedToday.AddDays(1))
            ]);
            var dashboard = await new GetAdminCareHomeDashboardHandler(readDb, occupancy)
                .Handle(new(null, null, null, FixedUtcNow), default);

            Assert.True(dashboard.IsSuccess);
            // 2 shared beds (facility A) + 1 physical room (facility B).
            Assert.Equal(3, dashboard.Value.TotalResources);
            Assert.Equal(1, dashboard.Value.OccupiedResources);
            Assert.Equal(2300m, dashboard.Value.PeriodRevenue.GrossCollected);
            Assert.Equal(2300m, dashboard.Value.PeriodRevenue.NetCollected);
        }
        finally
        {
            await using var cleanupDb = CreateDbContext(connectionString);
            if (bookingIdsToDelete.Count > 0)
            {
                await cleanupDb.Bookings.Where(x => bookingIdsToDelete.Contains(x.Id))
                    .ExecuteDeleteAsync();
            }
            if (facilityIdsToDelete.Count > 0)
            {
                await cleanupDb.Facilities.Where(x => facilityIdsToDelete.Contains(x.Id))
                    .ExecuteDeleteAsync();
            }
        }
    }

    [Fur03PostgresFact]
    public async Task Owner_Revenue_DraftFacility_ReturnsOwnData()
    {
        string connectionString = GetValidatedConnectionString();

        List<CareHomeId> facilityIdsToDelete = [];
        List<Guid> bookingIdsToDelete = [];

        try
        {
            await using var setupDb = CreateDbContext(connectionString);

            UserId owner = UserId.New();
            CareHomeFacility facility = CareHomeFacility.CreateDraft(owner, FixedUtcNow);
            setupDb.Facilities.Add(facility);
            await setupDb.SaveChangesAsync();

            facilityIdsToDelete.Add(facility.Id);

            Assert.Equal(owner,
                (await setupDb.Facilities.SingleAsync(x => x.Id == facility.Id)).OwnerUserId);
            Assert.Equal(CareHomeStatus.Draft,
                (await setupDb.Facilities.SingleAsync(x => x.Id == facility.Id)).Status);

            Guid roomTypeId = Guid.NewGuid();
            CareHomeBooking booking = CreatePaidBooking(facility.Id, roomTypeId, 51, FixedUtcNow);
            setupDb.Bookings.Add(booking);
            await setupDb.SaveChangesAsync();

            bookingIdsToDelete.Add(booking.Id);

            await using var readDb = CreateDbContext(connectionString);
            var revenue = await new GetOwnerCareHomeRevenueHandler(readDb)
                .Handle(new(owner, null, null), default);

            Assert.True(revenue.IsSuccess);
            Assert.Equal(1150m, revenue.Value.Totals.GrossCollected);
            Assert.Equal(1150m, revenue.Value.Totals.NetCollected);
            Assert.Equal(1, revenue.Value.Totals.PaymentCount);
            Assert.Single(revenue.Value.Transactions);
            Assert.Equal(facility.Id.Value, revenue.Value.Transactions[0].FacilityId);
        }
        finally
        {
            await using var cleanupDb = CreateDbContext(connectionString);
            if (bookingIdsToDelete.Count > 0)
            {
                await cleanupDb.Bookings.Where(x => bookingIdsToDelete.Contains(x.Id))
                    .ExecuteDeleteAsync();
            }
            if (facilityIdsToDelete.Count > 0)
            {
                await cleanupDb.Facilities.Where(x => facilityIdsToDelete.Contains(x.Id))
                    .ExecuteDeleteAsync();
            }
        }
    }

    [Fur03PostgresFact]
    public async Task Owner_Revenue_ApprovedFacility_ReturnsOwnData()
    {
        string connectionString = GetValidatedConnectionString();

        List<CareHomeId> facilityIdsToDelete = [];
        List<Guid> bookingIdsToDelete = [];

        try
        {
            await using var setupDb = CreateDbContext(connectionString);

            UserId owner = UserId.New();
            UserId reviewer = UserId.New();
            CareHomeFacility facility = CareHomeFacility.CreateDraft(owner, FixedUtcNow);
            setupDb.Facilities.Add(facility);
            await setupDb.SaveChangesAsync();

            ApproveFacility(setupDb, facility, owner, reviewer, FixedUtcNow);
            await setupDb.SaveChangesAsync();

            facilityIdsToDelete.Add(facility.Id);

            Assert.Equal(owner,
                (await setupDb.Facilities.SingleAsync(x => x.Id == facility.Id)).OwnerUserId);
            Assert.Equal(CareHomeStatus.Approved,
                (await setupDb.Facilities.SingleAsync(x => x.Id == facility.Id)).Status);

            Guid roomTypeId = Guid.NewGuid();
            CareHomeBooking booking = CreatePaidBooking(facility.Id, roomTypeId, 61, FixedUtcNow);
            setupDb.Bookings.Add(booking);
            await setupDb.SaveChangesAsync();

            bookingIdsToDelete.Add(booking.Id);

            await using var readDb = CreateDbContext(connectionString);
            var revenue = await new GetOwnerCareHomeRevenueHandler(readDb)
                .Handle(new(owner, null, null), default);

            Assert.True(revenue.IsSuccess);
            Assert.Equal(1150m, revenue.Value.Totals.GrossCollected);
            Assert.Equal(1150m, revenue.Value.Totals.NetCollected);
            Assert.Equal(1, revenue.Value.Totals.PaymentCount);
            Assert.Single(revenue.Value.Transactions);
            Assert.Equal(facility.Id.Value, revenue.Value.Transactions[0].FacilityId);
        }
        finally
        {
            await using var cleanupDb = CreateDbContext(connectionString);
            if (bookingIdsToDelete.Count > 0)
            {
                await cleanupDb.Bookings.Where(x => bookingIdsToDelete.Contains(x.Id))
                    .ExecuteDeleteAsync();
            }
            if (facilityIdsToDelete.Count > 0)
            {
                await cleanupDb.Facilities.Where(x => facilityIdsToDelete.Contains(x.Id))
                    .ExecuteDeleteAsync();
            }
        }
    }

    [Fur03PostgresFact]
    public async Task Owner_Dashboard_ApprovedFacility_ReturnsData()
    {
        string connectionString = GetValidatedConnectionString();

        List<CareHomeId> facilityIdsToDelete = [];

        try
        {
            await using var setupDb = CreateDbContext(connectionString);

            UserId owner = UserId.New();
            UserId reviewer = UserId.New();
            CareHomeFacility facility = CareHomeFacility.CreateDraft(owner, FixedUtcNow);
            setupDb.Facilities.Add(facility);
            await setupDb.SaveChangesAsync();

            ApproveFacility(setupDb, facility, owner, reviewer, FixedUtcNow);
            await setupDb.SaveChangesAsync();

            facilityIdsToDelete.Add(facility.Id);

            Assert.Equal(CareHomeStatus.Approved,
                (await setupDb.Facilities.SingleAsync(x => x.Id == facility.Id)).Status);

            Guid roomTypeId = Guid.NewGuid();
            CareHomeBooking booking = CreatePaidBooking(facility.Id, roomTypeId, 71, FixedUtcNow);
            setupDb.Bookings.Add(booking);
            await setupDb.SaveChangesAsync();

            await using var readDb = CreateDbContext(connectionString);
            var dashboard = await new GetOwnerCareHomeDashboardHandler(readDb, new FixedOccupancyProvider([]))
                .Handle(new(owner, null, null, FixedUtcNow), default);

            Assert.True(dashboard.IsSuccess);
            Assert.Equal(0, dashboard.Value.TotalResources);
            Assert.Equal(0, dashboard.Value.OccupiedResources);
            Assert.Equal(0m, dashboard.Value.OccupancyPercentage);
            Assert.Equal(1150m, dashboard.Value.PeriodRevenue.GrossCollected);
            Assert.Equal(1150m, dashboard.Value.PeriodRevenue.NetCollected);
            Assert.Equal(1, dashboard.Value.PeriodRevenue.PaymentCount);
        }
        finally
        {
            await using var cleanupDb = CreateDbContext(connectionString);
            if (facilityIdsToDelete.Count > 0)
            {
                await cleanupDb.Facilities.Where(x => facilityIdsToDelete.Contains(x.Id))
                    .ExecuteDeleteAsync();
            }
        }
    }

    [Fur03PostgresFact]
    public async Task Owner_Revenue_NoFacility_ReturnsNotFound()
    {
        string connectionString = GetValidatedConnectionString();

        await using var db = CreateDbContext(connectionString);

        UserId ownerWithoutFacility = UserId.New();

        var revenue = await new GetOwnerCareHomeRevenueHandler(db)
            .Handle(new(ownerWithoutFacility, null, null), default);

        Assert.False(revenue.IsSuccess);
        Assert.Equal(CareHomeRevenueProjection.NotFound.Code, revenue.Error.Code);
    }

    [Fur03PostgresFact]
    public async Task Owner_Dashboard_EmptyDraftFacility_ReturnsZeroTotals()
    {
        string connectionString = GetValidatedConnectionString();

        List<CareHomeId> facilityIdsToDelete = [];

        try
        {
            await using var setupDb = CreateDbContext(connectionString);

            UserId owner = UserId.New();
            CareHomeFacility facility = CareHomeFacility.CreateDraft(owner, FixedUtcNow);
            setupDb.Facilities.Add(facility);
            await setupDb.SaveChangesAsync();

            facilityIdsToDelete.Add(facility.Id);

            await using var readDb = CreateDbContext(connectionString);
            var dashboard = await new GetOwnerCareHomeDashboardHandler(readDb, new FixedOccupancyProvider([]))
                .Handle(new(owner, null, null, FixedUtcNow), default);

            Assert.True(dashboard.IsSuccess);
            Assert.Equal(0, dashboard.Value.TotalResources);
            Assert.Equal(0, dashboard.Value.OccupiedResources);
            Assert.Equal(0m, dashboard.Value.OccupancyPercentage);
            Assert.Equal(0m, dashboard.Value.PeriodRevenue.GrossCollected);
        }
        finally
        {
            await using var cleanupDb = CreateDbContext(connectionString);
            if (facilityIdsToDelete.Count > 0)
            {
                await cleanupDb.Facilities.Where(x => facilityIdsToDelete.Contains(x.Id))
                    .ExecuteDeleteAsync();
            }
        }
    }

    [Fur03PostgresFact]
    public async Task Owner_Dashboard_DraftFacilityWithActivity_ReturnsRealData()
    {
        // Portal second pass A2: the owner dashboard must allow a Draft
        // facility and retain its real activity (revenue and resource counts),
        // not report zero/empty values. Draft-with-no-activity coverage stays
        // in Owner_Dashboard_EmptyDraftFacility_ReturnsZeroTotals.
        string connectionString = GetValidatedConnectionString();

        List<CareHomeId> facilityIdsToDelete = [];
        List<Guid> bookingIdsToDelete = [];

        try
        {
            await using var setupDb = CreateDbContext(connectionString);

            UserId owner = UserId.New();
            CareHomeFacility facility = CareHomeFacility.CreateDraft(owner, FixedUtcNow);
            setupDb.Facilities.Add(facility);
            await setupDb.SaveChangesAsync();

            facilityIdsToDelete.Add(facility.Id);
            Assert.Equal(CareHomeStatus.Draft,
                (await setupDb.Facilities.SingleAsync(x => x.Id == facility.Id)).Status);

            CareHomeRoomType sharedType = CareHomeRoomType.Create(
                facility.Id, "مشترك D", "Shared D", 1000m, CareHomeAllocationMode.Shared, FixedUtcNow);
            CareHomeRoom room = CareHomeRoom.Create(facility.Id, sharedType.Id, "RD1", FixedUtcNow);
            CareHomeBed bed = CareHomeBed.Create(facility.Id, room.Id, "D1", FixedUtcNow);
            setupDb.RoomTypes.Add(sharedType);
            setupDb.Rooms.Add(room);
            setupDb.Beds.Add(bed);

            Guid roomTypeId = sharedType.Id;
            CareHomeBooking booking = CreatePaidBooking(facility.Id, roomTypeId, 91, FixedUtcNow);
            setupDb.Bookings.Add(booking);
            await setupDb.SaveChangesAsync();

            bookingIdsToDelete.Add(booking.Id);

            // Readback prerequisites before the scenario: exactly one accepted,
            // paid booking carries the facility's activity.
            Assert.Equal(1, await setupDb.Bookings.CountAsync(
                x => x.FacilityId == facility.Id && x.Status == CareHomeBookingStatus.Accepted));

            await using var readDb = CreateDbContext(connectionString);
            var occupancy = new FixedOccupancyProvider(
            [
                new(bed.Id, CareHomeResourceKind.Bed, FixedToday, FixedToday.AddDays(1))
            ]);
            var dashboard = await new GetOwnerCareHomeDashboardHandler(readDb, occupancy)
                .Handle(new(owner, null, null, FixedUtcNow), default);

            Assert.True(dashboard.IsSuccess);
            Assert.Equal(1, dashboard.Value.TotalResources);
            Assert.Equal(1, dashboard.Value.OccupiedResources);
            Assert.Equal(100m, dashboard.Value.OccupancyPercentage);
            Assert.Equal(1150m, dashboard.Value.PeriodRevenue.GrossCollected);
            Assert.Equal(1150m, dashboard.Value.PeriodRevenue.NetCollected);
            Assert.Equal(1, dashboard.Value.PeriodRevenue.PaymentCount);
        }
        finally
        {
            await using var cleanupDb = CreateDbContext(connectionString);
            if (bookingIdsToDelete.Count > 0)
            {
                await cleanupDb.Bookings.Where(x => bookingIdsToDelete.Contains(x.Id))
                    .ExecuteDeleteAsync();
            }
            if (facilityIdsToDelete.Count > 0)
            {
                await cleanupDb.Facilities.Where(x => facilityIdsToDelete.Contains(x.Id))
                    .ExecuteDeleteAsync();
            }
        }
    }

    [Fur03PostgresFact]
    public async Task Revenue_DateRange_FiltersByCairoLocalInclusiveDates()
    {
        string connectionString = GetValidatedConnectionString();

        List<CareHomeId> facilityIdsToDelete = [];
        List<Guid> bookingIdsToDelete = [];

        try
        {
            await using var setupDb = CreateDbContext(connectionString);

            UserId owner = UserId.New();
            CareHomeFacility facility = CareHomeFacility.CreateDraft(owner, FixedUtcNow);
            setupDb.Facilities.Add(facility);
            await setupDb.SaveChangesAsync();

            facilityIdsToDelete.Add(facility.Id);

            Guid roomTypeId = Guid.NewGuid();
            DateOnly paymentDay = FixedToday;
            string day = paymentDay.ToString("yyyy-MM-dd");
            string future = paymentDay.AddDays(30).ToString("yyyy-MM-dd");

            CareHomeBooking inRange = CreatePaidBooking(facility.Id, roomTypeId, 81, FixedUtcNow);
            CareHomeBooking outOfRange = CreatePaidBooking(facility.Id, roomTypeId, 82,
                FixedUtcNow.AddDays(-40));

            setupDb.Bookings.AddRange(inRange, outOfRange);
            await setupDb.SaveChangesAsync();

            bookingIdsToDelete.Add(inRange.Id);
            bookingIdsToDelete.Add(outOfRange.Id);

            await using var readDb = CreateDbContext(connectionString);

            var inRangeResult = await new GetAdminCareHomeRevenueHandler(readDb)
                .Handle(new(facility.Id.Value, paymentDay, paymentDay), default);
            var outOfRangeResult = await new GetAdminCareHomeRevenueHandler(readDb)
                .Handle(new(facility.Id.Value, paymentDay.AddDays(30), paymentDay.AddDays(30)), default);

            Assert.True(inRangeResult.IsSuccess);
            Assert.Equal(1, inRangeResult.Value.Totals.PaymentCount);
            Assert.Equal(1150m, inRangeResult.Value.Totals.GrossCollected);
            Assert.Single(inRangeResult.Value.Transactions);
            Assert.Equal(inRange.Id, inRangeResult.Value.Transactions[0].BookingId);

            Assert.True(outOfRangeResult.IsSuccess);
            Assert.Equal(0, outOfRangeResult.Value.Totals.PaymentCount);
            Assert.Equal(0m, outOfRangeResult.Value.Totals.GrossCollected);
            Assert.Empty(outOfRangeResult.Value.Transactions);
        }
        finally
        {
            await using var cleanupDb = CreateDbContext(connectionString);
            if (bookingIdsToDelete.Count > 0)
            {
                await cleanupDb.Bookings.Where(x => bookingIdsToDelete.Contains(x.Id))
                    .ExecuteDeleteAsync();
            }
            if (facilityIdsToDelete.Count > 0)
            {
                await cleanupDb.Facilities.Where(x => facilityIdsToDelete.Contains(x.Id))
                    .ExecuteDeleteAsync();
            }
        }
    }

    [Fur03PostgresFact]
    public async Task Revenue_InvalidRange_ReturnsInvalidRangeError()
    {
        string connectionString = GetValidatedConnectionString();

        List<CareHomeId> facilityIdsToDelete = [];

        try
        {
            await using var setupDb = CreateDbContext(connectionString);

            UserId owner = UserId.New();
            CareHomeFacility facility = CareHomeFacility.CreateDraft(owner, FixedUtcNow);
            setupDb.Facilities.Add(facility);
            await setupDb.SaveChangesAsync();

            facilityIdsToDelete.Add(facility.Id);

            await using var readDb = CreateDbContext(connectionString);

            var reversed = await new GetAdminCareHomeRevenueHandler(readDb)
                .Handle(new(facility.Id.Value, paymentDayAfter(), paymentDayBefore()), default);

            Assert.False(reversed.IsSuccess);
            Assert.Equal(CareHomeRevenueProjection.InvalidRange.Code, reversed.Error.Code);
        }
        finally
        {
            await using var cleanupDb = CreateDbContext(connectionString);
            if (facilityIdsToDelete.Count > 0)
            {
                await cleanupDb.Facilities.Where(x => facilityIdsToDelete.Contains(x.Id))
                    .ExecuteDeleteAsync();
            }
        }
    }

    private static DateOnly paymentDayAfter() => new(2026, 10, 9);
    private static DateOnly paymentDayBefore() => new(2026, 10, 8);

    private static CareHomeBooking CreatePaidBooking(
        CareHomeId facilityId, Guid roomTypeId, long transactionId, DateTime completedOnUtc)
    {
        CareHomeBooking booking = CareHomeBooking.Create(
            facilityId, UserId.New(), FamilyId.New(), ElderlyId.New(), roomTypeId,
            new DateOnly(2026, 10, 10), 1000m, 100m, 50m, 4,
            "رعاية", "Care", 75, null, "Contact", null, null, null, FixedUtcNow);
        booking.MarkPaid(transactionId, completedOnUtc);
        booking.Accept(completedOnUtc);
        return booking;
    }

    private static CareHomeBooking CreatePaidAndRefundedBooking(
        CareHomeId facilityId, Guid roomTypeId, long transactionId,
        DateTime paymentCompletedOnUtc, DateTime refundCompletedOnUtc)
    {
        CareHomeBooking booking = CareHomeBooking.Create(
            facilityId, UserId.New(), FamilyId.New(), ElderlyId.New(), roomTypeId,
            new DateOnly(2026, 10, 10), 1000m, 100m, 50m, 4,
            "رعاية", "Care", 75, null, "Contact", null, null, null, FixedUtcNow);
        booking.MarkPaid(transactionId, paymentCompletedOnUtc);
        booking.Accept(paymentCompletedOnUtc);
        booking.CancelByFamily(refundCompletedOnUtc);
        booking.MarkRefundManuallyCompleted($"REF-{transactionId}", "Test refund", UserId.New(),
            refundCompletedOnUtc);
        return booking;
    }

    private static void ApproveFacility(
        CareHomesDbContext db, CareHomeFacility facility, UserId owner, UserId reviewer, DateTime now)
    {
        facility.SaveDraft(owner, facility.Version, ProfileDraft, now);
        int version = facility.Version;
        foreach (CareHomeDocumentType type in Enum.GetValues<CareHomeDocumentType>())
        {
            facility.UploadDocument(owner, version, type, $"private/fur03-{type}.pdf",
                "application/pdf", 10, null, now);
            version = facility.Version;
        }

        facility.Submit(owner, facility.Version, now);
        foreach (CareHomeDocument document in facility.Documents.ToArray())
        {
            facility.VerifyDocument(reviewer, facility.Version, document.Id, null, true, now);
        }

        facility.Review(reviewer, facility.Version, CareHomeReviewAction.Approved, null, now,
            DateOnly.FromDateTime(now));
    }

    private static string GetValidatedConnectionString()
    {
        string? raw = Environment.GetEnvironmentVariable(ConnectionStringVariable);

        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new InvalidOperationException(
                $"Test requires {ConnectionStringVariable}.");
        }

        var builder = new NpgsqlConnectionStringBuilder(raw);

        if (builder.Host is not ("localhost" or "127.0.0.1" or "::1"))
        {
            throw new InvalidOperationException(
                "Refusing to write: the FUR-03 verification target " +
                "must be a local disposable PostgreSQL instance.");
        }

        if (!string.Equals(builder.Database, ExpectedDatabase, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Refusing to write: the FUR-03 verification target " +
                $"must be database '{ExpectedDatabase}'.");
        }

        return builder.ConnectionString;
    }

    private static CareHomesDbContext CreateDbContext(string connectionString)
    {
        DbContextOptions<CareHomesDbContext> options =
            new DbContextOptionsBuilder<CareHomesDbContext>()
                .UseNpgsql(connectionString).Options;

        return new CareHomesDbContext(options);
    }

    private sealed class FixedOccupancyProvider(
        IReadOnlyList<CareHomeOccupancyInterval> intervals) : ICareHomeOccupancyProvider
    {
        public Task<IReadOnlyList<CareHomeOccupancyInterval>> GetActiveAsync(
            CareHomeId facilityId, CancellationToken cancellationToken) =>
            Task.FromResult(intervals);
    }

    private sealed class Fur03PostgresFactAttribute : FactAttribute
    {
        public Fur03PostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable(ConnectionStringVariable)))
            {
                Skip =
                    "FUR-03 PostgreSQL proof is skipped. Set " +
                    "ConnectionStrings__Fur03VerificationDatabase " +
                    "to a validated disposable target to run it.";
            }
        }
    }
}

[CollectionDefinition("Fur03Verification", DisableParallelization = true)]
public sealed class Fur03VerificationCollection
{
}
