using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.CareHomes.Application.Abstractions.Data;
using Sanad.Modules.CareHomes.Application.Visits;
using Sanad.Modules.CareHomes.Domain.Bookings;
using Sanad.Modules.CareHomes.Domain.Facilities;
using Sanad.Modules.CareHomes.Infrastructure.Persistence;
using Sanad.Modules.Identity.Application.Abstractions.Security;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.UnitTests.API;

/// <summary>
/// PORTAL-05 bounded harness. Reuses the isolated <see cref="PortalApiMetadataHost"/>
/// (dummy connections, disabled migrations/seeders, no background jobs, ephemeral
/// keys, temporary storage, production auth/policies/MVC/JSON/dispatch) and seeds
/// only the minimum synthetic Care Homes state on a fresh InMemory store per test.
/// Real query/command handlers execute over HTTP; only the PostgreSQL advisory-lock
/// visit reservation guard is inlined (single-threaded verification, no concurrency
/// claim). No database network, no production target, no provider sends.
/// </summary>
internal static class CareHomeOwnerPortalFixture
{
    internal static CareHomesDbContext CreateDb() => new(
        new DbContextOptionsBuilder<CareHomesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    internal static User CreateUser(AccountType accountType, int index)
    {
        User user = User.Create(
            FullName.Create("مالك"),
            FullName.Create($"Portal Owner {index}"),
            Email.Create($"portalowner{index}@example.test"),
            PhoneNumber.Create($"+20100000{index:D4}"));
        user.AddAccount(accountType);
        return user;
    }

    internal static string CreateToken(PortalApiMetadataHost host, User user)
    {
        IAuthTokenService tokens = host.Services.GetRequiredService<IAuthTokenService>();
        return tokens.GenerateAccessToken(user, DateTime.UtcNow.AddSeconds(-5)).PlainTextToken;
    }

    internal static WebApplicationFactory<Program> OverrideCareHomes(
        PortalApiMetadataHost host, CareHomesDbContext db) =>
        host.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<CareHomesDbContext>();
            services.RemoveAll<ICareHomesDbContext>();
            services.AddSingleton(db);
            services.AddSingleton<ICareHomesDbContext>(db);
            services.RemoveAll<ICareHomeVisitReservationGuard>();
            services.AddSingleton<ICareHomeVisitReservationGuard, InlineVisitGuard>();
        }));

    internal static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

    internal static CareHomeProfileDraft ProfileDraft() => new(
        "دار الأمل",
        "Hope House",
        "رعاية وإقامة آمنة",
        "Safe care and accommodation",
        "Contact name",
        "+201000000000",
        null,
        "Cairo",
        "Nasr City",
        "District 1",
        "Example street address",
        "الشروط",
        "Admission conditions",
        [],
        []);

    internal static CareHomeFacility SeedFacility(CareHomesDbContext db, UserId owner, DateTime now, bool withProfile)
    {
        CareHomeFacility facility = CareHomeFacility.CreateDraft(owner, now);
        db.Facilities.Add(facility);
        db.SaveChanges();
        if (withProfile)
        {
            facility.SaveDraft(owner, facility.Version, ProfileDraft(), now);
            db.SaveChanges();
        }

        return facility;
    }

    internal sealed record InventorySeed(
        CareHomeRoomType RoomType, CareHomeRoom Room, CareHomeBed Bed);

    internal static InventorySeed SeedInventory(
        CareHomesDbContext db, CareHomeId facilityId, DateTime now,
        string roomNumber = "101", string bedLabel = "A")
    {
        CareHomeRoomType type = CareHomeRoomType.Create(
            facilityId, "غرفة مشتركة", "Shared room", 1000m, CareHomeAllocationMode.Shared, now);
        CareHomeRoom room = CareHomeRoom.Create(facilityId, type.Id, roomNumber, now);
        CareHomeBed bed = CareHomeBed.Create(facilityId, room.Id, bedLabel, now);
        db.RoomTypes.Add(type);
        db.Rooms.Add(room);
        db.Beds.Add(bed);
        db.SaveChanges();
        return new(type, room, bed);
    }

    internal static CareHomeBooking SeedPaidBooking(
        CareHomesDbContext db, CareHomeId facilityId, Guid roomTypeId, DateTime now,
        long transactionId, DateOnly? startDate = null, bool accept = true)
    {
        DateOnly start = startDate ?? DateOnly.FromDateTime(now.AddDays(10));
        CareHomeBooking booking = CareHomeBooking.Create(
            facilityId, UserId.New(), FamilyId.New(), ElderlyId.New(), roomTypeId, start,
            1000m, 100m, 50m, 4, "رعاية", "Care", 75, null, "Contact",
            "+201000000000", null, null, now);
        booking.MarkPaid(transactionId, now);
        if (accept)
        {
            booking.Accept(now);
        }

        db.Bookings.Add(booking);
        db.SaveChanges();
        return booking;
    }

    internal static CareHomeBooking SeedPendingBooking(
        CareHomesDbContext db, CareHomeId facilityId, Guid roomTypeId, DateTime now,
        DateOnly? startDate = null)
    {
        DateOnly start = startDate ?? DateOnly.FromDateTime(now.AddDays(10));
        CareHomeBooking booking = CareHomeBooking.Create(
            facilityId, UserId.New(), FamilyId.New(), ElderlyId.New(), roomTypeId, start,
            1000m, 100m, 50m, 4, "رعاية", "Care", 75, null, "Contact",
            "+201000000000", null, null, now);
        db.Bookings.Add(booking);
        db.SaveChanges();
        return booking;
    }

    internal static CareHomeVisit SeedVisit(
        CareHomesDbContext db, CareHomeId facilityId, DateTime now, DateTime startsAtUtc,
        string visitorName = "Portal Visitor")
    {
        CareHomeVisit visit = CareHomeVisit.Request(
            facilityId, FamilyId.New(), UserId.New(), CareHomeVisitKind.Prospective, null,
            visitorName, "+201000000000", 2, startsAtUtc, now);
        db.Visits.Add(visit);
        db.SaveChanges();
        return visit;
    }

    internal sealed class InlineVisitGuard : ICareHomeVisitReservationGuard
    {
        public Task<T> ExecuteAsync<T>(
            CareHomeId facilityId, Func<Task<T>> action, CancellationToken cancellationToken) =>
            action();
    }
}
