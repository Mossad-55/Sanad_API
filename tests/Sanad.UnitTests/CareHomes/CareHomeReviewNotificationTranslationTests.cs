using Microsoft.EntityFrameworkCore;
using Sanad.API.CareHomesIntegration;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.Modules.Identity.Infrastructure.Persistence;

namespace Sanad.UnitTests.CareHomes;

/// <summary>
/// Regression for the recurring CareHomeReviewNotificationProcessor PostgreSQL
/// failure (<c>Where(u =&gt; u.Id.Value == @ownerId)</c> could not be translated).
/// The Npgsql facts below generate SQL from the exact production query
/// composition without opening any connection, so they prove PostgreSQL
/// translation rather than InMemory behavior. The InMemory facts prove the
/// recipient-role/email/language mapping preserved by the fix.
/// </summary>
public sealed class CareHomeReviewNotificationTranslationTests
{
    private static readonly DateTime Now = new(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);

    private static IdentityDbContext CreateNpgsqlDb()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql("Host=localhost;Database=translation_probe;Username=probe;Password=probe")
            .Options;
        return new IdentityDbContext(options);
    }

    [Fact]
    public void Owner_recipient_rows_translate_on_postgresql()
    {
        using var db = CreateNpgsqlDb();

        string sql = CareHomeReviewNotificationProcessor
            .OwnerRecipientRows(db, new UserId(Guid.NewGuid()))
            .ToQueryString();

        Assert.Contains("WHERE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Operational_admin_recipient_rows_translate_on_postgresql()
    {
        using var db = CreateNpgsqlDb();

        string sql = CareHomeReviewNotificationProcessor
            .OperationalAdminRecipientRows(db)
            .ToQueryString();

        Assert.Contains("WHERE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("user_accounts", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Find_owner_recipients_returns_exact_owner_with_email_and_language()
    {
        await using var db = CreateIdentityDb();
        User owner = AddUser(db, AccountType.CareHomeOwner, "owner@example.test", UiLanguage.English);
        AddUser(db, AccountType.CareHomeOwner, "other@example.test", UiLanguage.Arabic);
        await db.SaveChangesAsync();

        var recipients = await CareHomeReviewNotificationProcessor
            .FindOwnerRecipientsAsync(db, owner.Id, CancellationToken.None);

        var recipient = Assert.Single(recipients);
        Assert.Equal(owner.Id.Value, recipient.Id);
        Assert.Equal("owner@example.test", recipient.Email);
        Assert.Equal(UiLanguage.English, recipient.UiLanguage);
    }

    [Fact]
    public async Task Find_owner_recipients_maps_missing_email_to_null()
    {
        await using var db = CreateIdentityDb();
        User emailLess = AddUser(db, AccountType.Elderly, null, UiLanguage.Arabic);
        await db.SaveChangesAsync();

        var recipients = await CareHomeReviewNotificationProcessor
            .FindOwnerRecipientsAsync(db, emailLess.Id, CancellationToken.None);

        var recipient = Assert.Single(recipients);
        Assert.Equal(emailLess.Id.Value, recipient.Id);
        Assert.Null(recipient.Email);
        Assert.Equal(UiLanguage.Arabic, recipient.UiLanguage);
    }

    [Fact]
    public async Task Find_operational_admin_recipients_returns_only_active_super_and_support_admins()
    {
        await using var db = CreateIdentityDb();
        User super = AddUser(db, AccountType.SuperAdmin, "super@example.test", UiLanguage.Arabic);
        User support = AddUser(db, AccountType.SupportAdmin, "support@example.test", UiLanguage.English);
        User content = AddUser(db, AccountType.ContentAdmin, "content@example.test", UiLanguage.Arabic);
        User owner = AddUser(db, AccountType.CareHomeOwner, "owner@example.test", UiLanguage.Arabic);
        User suspended = AddUser(db, AccountType.SuperAdmin, "suspended@example.test", UiLanguage.Arabic);
        suspended.Suspend("review", Now);
        await db.SaveChangesAsync();

        var recipients = await CareHomeReviewNotificationProcessor
            .FindOperationalAdminRecipientsAsync(db, CancellationToken.None);

        Assert.Equal(2, recipients.Count);
        Assert.Contains(recipients, x => x.Id == super.Id.Value && x.Email == "super@example.test");
        Assert.Contains(recipients, x => x.Id == support.Id.Value && x.UiLanguage == UiLanguage.English);
        Assert.DoesNotContain(recipients, x => x.Id == content.Id.Value);
        Assert.DoesNotContain(recipients, x => x.Id == owner.Id.Value);
        Assert.DoesNotContain(recipients, x => x.Id == suspended.Id.Value);
    }

    private static IdentityDbContext CreateIdentityDb() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static User AddUser(IdentityDbContext db, AccountType accountType, string? email, UiLanguage language)
    {
        User user = User.Create(FullName.Create("Review"), FullName.Create("Recipient"),
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
