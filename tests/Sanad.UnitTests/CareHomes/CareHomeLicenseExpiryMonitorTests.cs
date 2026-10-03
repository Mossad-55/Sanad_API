using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sanad.API.CareHomesIntegration;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.CareHomes.Domain.Facilities;
using Sanad.Modules.CareHomes.Infrastructure.Persistence;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.Modules.Identity.Infrastructure.Persistence;
using Sanad.Modules.Notifications.Domain.Notifications;
using Sanad.Modules.Notifications.Infrastructure.Persistence;
using System.Reflection;

namespace Sanad.UnitTests.CareHomes;

public sealed class CareHomeLicenseExpiryMonitorTests
{
    [Fact]
    public async Task Scan_AlertsOnlyForExpiredLatestVerifiedLicenseAndActiveOperationalAdmins()
    {
        DateOnly today = new(2026, 10, 2);
        DateTime now = today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        await using TestHost host = await TestHost.CreateAsync();

        User superAdmin = AddAdmin(host.Identity, AccountType.SuperAdmin, UserStatus.Active, "super@example.test", now);
        User supportAdmin = AddAdmin(host.Identity, AccountType.SupportAdmin, UserStatus.Active, "support@example.test", now);
        AddAdmin(host.Identity, AccountType.ContentAdmin, UserStatus.Active, "content@example.test", now);
        AddAdmin(host.Identity, AccountType.SuperAdmin, UserStatus.Suspended, "suspended@example.test", now);
        await host.Identity.SaveChangesAsync();

        CareHomeFacility expired = CreateApprovedFacility(now, today.AddDays(-1), verifyLicense: true);
        CareHomeFacility boundary = CreateApprovedFacility(now, today, verifyLicense: true);
        CareHomeFacility future = CreateApprovedFacility(now, today.AddDays(1), verifyLicense: true);
        CareHomeFacility nonExpiring = CreateApprovedFacility(now, null, verifyLicense: true, confirmNonExpiring: true);
        CareHomeFacility unverified = CreateApprovedFacility(now, today.AddDays(-1), verifyLicense: false);
        CareHomeFacility rejected = CreateApprovedFacility(now, today.AddDays(-1), verifyLicense: false, rejectLicense: true);
        host.CareHomes.AddRange(expired, boundary, future, nonExpiring, unverified, rejected);
        await host.CareHomes.SaveChangesAsync();

        await host.RunScanAsync(today);
        await host.RunScanAsync(today);

        List<Notification> alerts = await host.Notifications.Notifications.AsNoTracking().ToListAsync();
        List<EmailOutboxMessage> emails = await host.Notifications.EmailOutboxMessages.AsNoTracking().ToListAsync();

        Assert.Equal(2, alerts.Count);
        Assert.Equal(2, emails.Count);
        Assert.Contains(alerts, x => x.RecipientUserId == superAdmin.Id.Value && x.DestinationEntityId == expired.Id.Value);
        Assert.Contains(alerts, x => x.RecipientUserId == supportAdmin.Id.Value && x.DestinationEntityId == expired.Id.Value);
        Assert.All(emails, x => Assert.Contains("expired", x.Body, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(alerts, x => x.DestinationEntityId == boundary.Id.Value);
        Assert.DoesNotContain(alerts, x => x.DestinationEntityId == future.Id.Value);
        Assert.DoesNotContain(alerts, x => x.DestinationEntityId == nonExpiring.Id.Value);
        Assert.DoesNotContain(alerts, x => x.DestinationEntityId == unverified.Id.Value);
        Assert.DoesNotContain(alerts, x => x.DestinationEntityId == rejected.Id.Value);
        Assert.Equal(2, alerts.Select(x => x.IdempotencyKey).Distinct().Count());
        Assert.Equal(2, emails.Select(x => x.IdempotencyKey).Distinct().Count());
    }

    [Fact]
    public async Task Scan_UsesLatestLicenseForApprovedRevision()
    {
        DateOnly today = new(2026, 10, 2);
        DateTime now = today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        await using TestHost host = await TestHost.CreateAsync();
        AddAdmin(host.Identity, AccountType.SuperAdmin, UserStatus.Active, "admin@example.test", now);
        await host.Identity.SaveChangesAsync();

        CareHomeFacility facility = CreateApprovedFacility(now, today.AddDays(5), verifyLicense: true, oldExpiredLicense: true);
        host.CareHomes.Add(facility);
        await host.CareHomes.SaveChangesAsync();

        await host.RunScanAsync(today);

        Assert.Empty(await host.Notifications.Notifications.ToListAsync());
        Assert.Empty(await host.Notifications.EmailOutboxMessages.ToListAsync());
    }

    private static CareHomeFacility CreateApprovedFacility(DateTime now, DateOnly? licenseExpiry,
        bool verifyLicense, bool confirmNonExpiring = false, bool rejectLicense = false, bool oldExpiredLicense = false)
    {
        UserId owner = UserId.New();
        UserId admin = UserId.New();
        CareHomeFacility facility = CareHomeFacility.CreateDraft(owner, now);
        facility.SaveDraft(owner, facility.Version, CareHomeFacilityTests.Draft(), now);
        if (oldExpiredLicense)
            facility.UploadDocument(owner, CareHomeDocumentType.OperatingLicense, "old-license.pdf", "application/pdf", 100, new DateOnly(2026, 10, 1), now);
        foreach (CareHomeDocumentType type in Enum.GetValues<CareHomeDocumentType>())
            facility.UploadDocument(owner, type, $"{type}-{Guid.NewGuid():N}.pdf", "application/pdf", 100, type == CareHomeDocumentType.OperatingLicense ? licenseExpiry : null, now.AddSeconds(type == CareHomeDocumentType.OperatingLicense ? 1 : 0));
        facility.Submit(owner, facility.Version, now.AddMinutes(1));
        foreach (CareHomeDocument document in facility.Documents)
        {
            if (document.Type == CareHomeDocumentType.OperatingLicense && rejectLicense)
                facility.RejectDocument(admin, facility.Version, document.Id, "invalid", now.AddMinutes(2));
            else if (document.Type == CareHomeDocumentType.OperatingLicense && !verifyLicense)
                continue;
            else
                facility.VerifyDocument(admin, facility.Version, document.Id, document.Type == CareHomeDocumentType.OperatingLicense ? licenseExpiry : null, document.Type == CareHomeDocumentType.OperatingLicense ? confirmNonExpiring : true, now.AddMinutes(2));
        }
        if (!rejectLicense && verifyLicense)
            facility.Review(admin, facility.Version, CareHomeReviewAction.Approved, null, now.AddMinutes(3), DateOnly.FromDateTime(now).AddDays(-2));
        return facility;
    }

    private static User AddAdmin(IdentityDbContext db, AccountType accountType, UserStatus status, string email, DateTime now)
    {
        User user = User.Create(FullName.Create("Admin"), FullName.Create("Admin"), Email.Create(email), PhoneNumber.Create("+201" + Random.Shared.NextInt64(100000000, 999999999)), null);
        user.AddAccount(accountType);
        user.VerifyEmail(now);
        user.VerifyPhone(now);
        user.SetInitialPasswordHash("test-password-hash", now);
        user.Activate(now);
        if (status != UserStatus.Active)
            user.Suspend("test", now);
        db.Users.Add(user);
        return user;
    }

    private sealed class TestHost : IAsyncDisposable
    {
        private readonly ServiceProvider services;
        public CareHomesDbContext CareHomes { get; }
        public IdentityDbContext Identity { get; }
        public NotificationsDbContext Notifications { get; }

        private TestHost(ServiceProvider services, CareHomesDbContext careHomes, IdentityDbContext identity, NotificationsDbContext notifications)
        { this.services = services; CareHomes = careHomes; Identity = identity; Notifications = notifications; }

        public static async Task<TestHost> CreateAsync()
        {
            ServiceCollection services = new();
            CareHomesDbContext careHomes = new(new DbContextOptionsBuilder<CareHomesDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            IdentityDbContext identity = new(new DbContextOptionsBuilder<IdentityDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            NotificationsDbContext notifications = new(new DbContextOptionsBuilder<NotificationsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            await careHomes.Database.EnsureCreatedAsync(); await identity.Database.EnsureCreatedAsync(); await notifications.Database.EnsureCreatedAsync();
            services.AddSingleton(careHomes).AddSingleton(identity).AddSingleton(notifications);
            ServiceProvider provider = services.BuildServiceProvider();
            return new TestHost(provider, careHomes, identity, notifications);
        }

        public async Task RunScanAsync(DateOnly date)
        {
            CareHomeLicenseExpiryMonitor monitor = new(services.GetRequiredService<IServiceScopeFactory>(), NullLogger<CareHomeLicenseExpiryMonitor>.Instance);
            MethodInfo method = typeof(CareHomeLicenseExpiryMonitor).GetMethod("RunOnceAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            await (Task)method.Invoke(monitor, [date, CancellationToken.None])!;
        }

        public async ValueTask DisposeAsync()
        { await CareHomes.DisposeAsync(); await Identity.DisposeAsync(); await Notifications.DisposeAsync(); await services.DisposeAsync(); }
    }
}
