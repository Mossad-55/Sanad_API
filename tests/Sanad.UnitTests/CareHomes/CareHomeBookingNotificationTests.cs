using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sanad.API.CareHomesIntegration;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.CareHomes.Domain.Bookings;
using Sanad.Modules.CareHomes.Domain.Facilities;
using Sanad.Modules.CareHomes.Infrastructure.Persistence;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.Modules.Identity.Infrastructure.Persistence;
using Sanad.Modules.Notifications.Infrastructure.Persistence;

namespace Sanad.UnitTests.CareHomes;

public sealed class CareHomeBookingNotificationTests
{
    private static readonly DateTime Now = new(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);
    private static DateTime EventNow => DateTime.UtcNow.AddMinutes(-1);

    [Fact]
    public async Task Owner_decision_event_is_localized_and_materialized_once_for_owner()
    {
        await using var host = await TestHost.CreateAsync();
        User owner = AddUser(host.Identity, AccountType.CareHomeOwner, "owner@example.test");
        User family = AddUser(host.Identity, AccountType.Family, "family@example.test");
        CareHomeBooking booking = AddBooking(host.CareHomes, owner, family);
        DateTime eventNow = EventNow;
        var row = CareHomeNotificationOutbox.Create("owner-decision-once", CareHomeNotificationEvent.OwnerDecisionNeeded,
            booking.Id, null, eventNow);
        host.CareHomes.BookingNotificationOutbox.Add(row);
        await host.Identity.SaveChangesAsync();
        await host.CareHomes.SaveChangesAsync();

        await host.ProcessAsync();
        await host.ProcessAsync();

        var notices = await host.Notifications.Notifications.AsNoTracking().ToListAsync();
        var emails = await host.Notifications.EmailOutboxMessages.AsNoTracking().ToListAsync();
        Assert.Single(notices);
        Assert.Equal(owner.Id.Value, notices[0].RecipientUserId);
        Assert.Contains("بانتظار قرارك", notices[0].Title);
        Assert.DoesNotContain(notices, x => x.RecipientUserId == family.Id.Value);
        Assert.Single(emails);
        Assert.Equal(owner.Email!.Value, emails[0].RecipientEmail);
        Assert.Equal(CareHomeNotificationOutboxStatus.Completed, row.Status);
        Assert.DoesNotContain("private medical snapshot", notices[0].Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private care notes", notices[0].Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refund_failure_alerts_only_active_operational_admins()
    {
        await using var host = await TestHost.CreateAsync();
        User owner = AddUser(host.Identity, AccountType.CareHomeOwner, "owner@example.test");
        User family = AddUser(host.Identity, AccountType.Family, "family@example.test");
        User super = AddUser(host.Identity, AccountType.SuperAdmin, "super@example.test");
        User support = AddUser(host.Identity, AccountType.SupportAdmin, "support@example.test");
        AddUser(host.Identity, AccountType.ContentAdmin, "content@example.test");
        CareHomeBooking booking = AddBooking(host.CareHomes, owner, family);
        DateTime eventNow = EventNow;
        host.CareHomes.BookingNotificationOutbox.Add(CareHomeNotificationOutbox.Create("refund-failure-once",
            CareHomeNotificationEvent.RefundFailed, booking.Id, null, eventNow));
        await host.Identity.SaveChangesAsync();
        await host.CareHomes.SaveChangesAsync();

        await host.ProcessAsync();

        var notices = await host.Notifications.Notifications.AsNoTracking().ToListAsync();
        var emails = await host.Notifications.EmailOutboxMessages.AsNoTracking().ToListAsync();
        Assert.Equal(2, notices.Count);
        Assert.Contains(notices, x => x.RecipientUserId == super.Id.Value);
        Assert.Contains(notices, x => x.RecipientUserId == support.Id.Value);
        Assert.DoesNotContain(notices, x => x.RecipientUserId == family.Id.Value || x.RecipientUserId == owner.Id.Value);
        Assert.Equal(2, emails.Count);
        Assert.Contains(emails, x => x.RecipientEmail == "super@example.test");
        Assert.Contains(emails, x => x.RecipientEmail == "support@example.test");
    }

    [Fact]
    public void Outbox_retry_is_claimed_and_completed_with_backoff()
    {
        var item = CareHomeNotificationOutbox.Create("decision-event", CareHomeNotificationEvent.DecisionAccepted,
            Guid.NewGuid(), null, Now);
        Guid claim = Guid.NewGuid();
        item.MarkProcessing(Now, claim);
        item.MarkFailed("temporary failure", Now, claim);

        Assert.Equal(CareHomeNotificationOutboxStatus.Failed, item.Status);
        Assert.Equal(Now.AddMinutes(5), item.NextAttemptOnUtc);

        item.MarkProcessing(item.NextAttemptOnUtc, Guid.NewGuid());
        Guid retryClaim = item.ClaimToken!.Value;
        item.MarkCompleted(retryClaim);
        Assert.Equal(CareHomeNotificationOutboxStatus.Completed, item.Status);
        Assert.Equal(2, item.AttemptCount);
    }

    [Fact]
    public void Reclaimed_outbox_event_rejects_completion_from_a_stale_worker()
    {
        var item = CareHomeNotificationOutbox.Create("stale-claim", CareHomeNotificationEvent.DecisionAccepted,
            Guid.NewGuid(), null, Now);
        Guid original = Guid.NewGuid();
        item.MarkProcessing(Now, original);
        Guid current = Guid.NewGuid();
        item.MarkProcessing(Now.AddMinutes(16), current);

        Assert.Throws<InvalidOperationException>(() => item.MarkCompleted(original));
        item.MarkCompleted(current);
        Assert.Equal(CareHomeNotificationOutboxStatus.Completed, item.Status);
        Assert.Equal(2, item.AttemptCount);
    }

    private static CareHomeBooking AddBooking(CareHomesDbContext db, User owner, User family)
    {
        CareHomeFacility facility = CareHomeFacility.CreateDraft(owner.Id, Now);
        db.Facilities.Add(facility);
        CareHomeBooking booking = CareHomeBooking.Create(facility.Id, family.Id, FamilyId.New(), ElderlyId.New(),
            Guid.NewGuid(), new DateOnly(2026, 10, 12), 100m, 1m, 0m, 1,
            "اسم", "Name", 70, "private medical snapshot", "Contact", null, null, "private care notes", Now);
        db.Bookings.Add(booking);
        return booking;
    }

    private static User AddUser(IdentityDbContext db, AccountType accountType, string? email)
    {
        User user = User.Create(FullName.Create("Test"), FullName.Create("User"),
            email is null ? null : Email.Create(email), PhoneNumber.Create("+201" + Random.Shared.NextInt64(100000000, 999999999)), null);
        user.AddAccount(accountType);
        if (email is not null) user.VerifyEmail(Now);
        user.VerifyPhone(Now);
        user.SetInitialPasswordHash("test-password-hash", Now);
        user.Activate(Now);
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
            var careHomes = new CareHomesDbContext(new DbContextOptionsBuilder<CareHomesDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var identity = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var notifications = new NotificationsDbContext(new DbContextOptionsBuilder<NotificationsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            await careHomes.Database.EnsureCreatedAsync(); await identity.Database.EnsureCreatedAsync(); await notifications.Database.EnsureCreatedAsync();
            var services = new ServiceCollection();
            services.AddSingleton(careHomes).AddSingleton(identity).AddSingleton(notifications);
            return new TestHost(services.BuildServiceProvider(), careHomes, identity, notifications);
        }

        public Task ProcessAsync() => new CareHomeBookingNotificationProcessor(
            services.GetRequiredService<IServiceScopeFactory>(), NullLogger<CareHomeBookingNotificationProcessor>.Instance)
            .ProcessBatchAsync(CancellationToken.None);

        public async ValueTask DisposeAsync()
        { await CareHomes.DisposeAsync(); await Identity.DisposeAsync(); await Notifications.DisposeAsync(); await services.DisposeAsync(); }
    }
}
