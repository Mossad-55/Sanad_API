using Microsoft.EntityFrameworkCore;
using Ahlena.API.CareHomesIntegration;
using Ahlena.BuildingBlocks.Domain.Enums;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.BuildingBlocks.Domain.ValueObjects;
using Ahlena.Modules.CareHomes.Domain.Bookings;
using Ahlena.Modules.Identity.Domain.Users;
using Ahlena.Modules.Identity.Infrastructure.Persistence;

namespace Ahlena.UnitTests.CareHomes;

/// <summary>
/// Regression for the unverified recipient predicate risk reported after
/// Prompt 3: <c>targets.Contains(x.Id.Value)</c> in
/// <c>CareHomeBookingNotificationProcessor.ResolveRecipientsAsync</c>.
/// The Npgsql facts generate SQL from the exact production query composition
/// without opening any connection, so they prove PostgreSQL translation rather
/// than InMemory behavior. The InMemory facts prove recipient selection and
/// mapping only.
/// </summary>
public sealed class CareHomeBookingNotificationTranslationTests
{
    private static readonly DateTime Now = new(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);

    private static IdentityDbContext CreateNpgsqlDb() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql("Host=localhost;Database=translation_probe;Username=probe;Password=probe").Options);

    [Fact]
    public void Target_recipient_rows_translate_on_postgresql()
    {
        using var db = CreateNpgsqlDb();

        string sql = CareHomeBookingNotificationProcessor
            .TargetRecipientRows(db, [new UserId(Guid.NewGuid()), new UserId(Guid.NewGuid())])
            .ToQueryString();

        Assert.Contains("WHERE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Operational_admin_recipient_rows_translate_on_postgresql()
    {
        using var db = CreateNpgsqlDb();

        string sql = CareHomeBookingNotificationProcessor
            .OperationalAdminRecipientRows(db)
            .ToQueryString();

        Assert.Contains("WHERE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("user_accounts", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Dispute_resolved_selects_family_and_owner_only()
    {
        await using var db = CreateIdentityDb();
        User owner = AddUser(db, AccountType.CareHomeOwner, "owner@example.test", UiLanguage.English);
        User family = AddUser(db, AccountType.Family, "family@example.test", UiLanguage.Arabic);
        User outsider = AddUser(db, AccountType.CareHomeOwner, "outsider@example.test", UiLanguage.Arabic);
        await db.SaveChangesAsync();

        var recipients = await CareHomeBookingNotificationProcessor.ResolveRecipientsAsync(
            db, CareHomeNotificationEvent.DisputeResolved, family.Id.Value, owner.Id.Value, CancellationToken.None);

        Assert.Equal(2, recipients.Count);
        Assert.Contains(recipients, x => x.Id == family.Id.Value && x.Email == "family@example.test");
        Assert.Contains(recipients, x => x.Id == owner.Id.Value && x.UiLanguage == UiLanguage.English);
        Assert.DoesNotContain(recipients, x => x.Id == outsider.Id.Value);
    }

    [Fact]
    public async Task Owner_decision_selects_owner_only_with_null_email_mapping()
    {
        await using var db = CreateIdentityDb();
        User owner = AddUser(db, AccountType.CareHomeOwner, "owner@example.test", UiLanguage.Arabic);
        User emailLessFamily = AddUser(db, AccountType.Elderly, null, UiLanguage.Arabic);
        await db.SaveChangesAsync();

        var ownerRecipients = await CareHomeBookingNotificationProcessor.ResolveRecipientsAsync(
            db, CareHomeNotificationEvent.OwnerDecisionNeeded, emailLessFamily.Id.Value, owner.Id.Value, CancellationToken.None);

        var single = Assert.Single(ownerRecipients);
        Assert.Equal(owner.Id.Value, single.Id);

        var familyRecipients = await CareHomeBookingNotificationProcessor.ResolveRecipientsAsync(
            db, CareHomeNotificationEvent.DecisionAccepted, emailLessFamily.Id.Value, owner.Id.Value, CancellationToken.None);

        var family = Assert.Single(familyRecipients);
        Assert.Equal(emailLessFamily.Id.Value, family.Id);
        Assert.Null(family.Email);
    }

    [Fact]
    public async Task Refund_failed_selects_only_active_super_and_support_admins()
    {
        await using var db = CreateIdentityDb();
        User super = AddUser(db, AccountType.SuperAdmin, "super@example.test", UiLanguage.Arabic);
        User support = AddUser(db, AccountType.SupportAdmin, "support@example.test", UiLanguage.English);
        AddUser(db, AccountType.ContentAdmin, "content@example.test", UiLanguage.Arabic);
        AddUser(db, AccountType.CareHomeOwner, "owner@example.test", UiLanguage.Arabic);
        User suspended = AddUser(db, AccountType.SuperAdmin, "suspended@example.test", UiLanguage.Arabic);
        suspended.Suspend("review", Now);
        await db.SaveChangesAsync();

        var recipients = await CareHomeBookingNotificationProcessor.ResolveRecipientsAsync(
            db, CareHomeNotificationEvent.RefundFailed, Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(2, recipients.Count);
        Assert.Contains(recipients, x => x.Id == super.Id.Value && x.Email == "super@example.test");
        Assert.Contains(recipients, x => x.Id == support.Id.Value && x.UiLanguage == UiLanguage.English);
    }

    private static IdentityDbContext CreateIdentityDb() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static User AddUser(IdentityDbContext db, AccountType accountType, string? email, UiLanguage language)
    {
        User user = User.Create(FullName.Create("Booking"), FullName.Create("Recipient"),
            email is null ? null : Email.Create(email),
            PhoneNumber.Create("+201" + Random.Shared.NextInt64(100000000, 999999999)), null);
        user.AddAccount(accountType);
        if (email is not null) user.VerifyEmail(Now);
        user.VerifyPhone(Now);
        user.SetInitialPasswordHash("test-password-hash", Now);
        user.Activate(Now);
        user.ChangeUiLanguage(language, Now);
        db.Users.Add(user);
        return user;
    }
}
