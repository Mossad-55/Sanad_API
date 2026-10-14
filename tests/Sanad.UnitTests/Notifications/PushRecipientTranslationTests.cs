using Microsoft.EntityFrameworkCore;
using Sanad.API.NotificationsIntegration;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.Modules.Identity.Infrastructure.Persistence;

namespace Sanad.UnitTests.Notifications;

/// <summary>
/// Regression for the unverified recipient predicate risk reported after
/// Prompt 3: <c>user.Id.Value == row.RecipientUserId</c> in
/// <c>PushOutboxProcessor</c> (both the <c>DeliverAsync</c> preference read and
/// the <c>IsChatPushEligibleAsync</c> final-send recheck). Both sites now
/// compose the single <c>RecipientUsers</c> query, so one Npgsql fact covers
/// the exact production composition of both. The Npgsql fact generates SQL
/// without opening any connection and is the PostgreSQL proof; the InMemory
/// fact proves selection only.
/// </summary>
public sealed class PushRecipientTranslationTests
{
    private static readonly DateTime Now = new(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);

    private static IdentityDbContext CreateNpgsqlDb() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql("Host=localhost;Database=translation_probe;Username=probe;Password=probe").Options);

    [Fact]
    public void Recipient_preference_read_translates_on_postgresql()
    {
        using var db = CreateNpgsqlDb();

        // Exact composition used by both DeliverAsync and IsChatPushEligibleAsync:
        // RecipientUsers(identity, recipient).Select(x => x.NotificationPreferences).
        string sql = PushOutboxProcessor
            .RecipientUsers(db, new UserId(Guid.NewGuid()))
            .Select(x => x.NotificationPreferences)
            .ToQueryString();

        Assert.Contains("WHERE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Recipient_users_selects_exact_recipient_only()
    {
        await using var db = CreateIdentityDb();
        User recipient = AddUser(db, "recipient@example.test");
        AddUser(db, "other@example.test");
        await db.SaveChangesAsync();

        var selected = await PushOutboxProcessor
            .RecipientUsers(db, recipient.Id)
            .ToListAsync();

        var single = Assert.Single(selected);
        Assert.Equal(recipient.Id, single.Id);
    }

    private static IdentityDbContext CreateIdentityDb() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static User AddUser(IdentityDbContext db, string email)
    {
        User user = User.Create(FullName.Create("Push"), FullName.Create("Recipient"),
            Email.Create(email),
            PhoneNumber.Create("+201" + Random.Shared.NextInt64(100000000, 999999999)), null);
        user.AddAccount(AccountType.Family);
        user.VerifyEmail(Now);
        user.VerifyPhone(Now);
        user.SetInitialPasswordHash("test-password-hash", Now);
        user.Activate(Now);
        user.ChangeUiLanguage(UiLanguage.Arabic, Now);
        db.Users.Add(user);
        return user;
    }
}
