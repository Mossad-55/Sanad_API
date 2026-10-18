using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Application.Users.Admin;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.Modules.Identity.Infrastructure.Persistence;

namespace Sanad.UnitTests.Identity;

/// <summary>
/// Focused E01 evidence for the SuperAdmin user-statistics aggregates:
/// distinct totals, all statuses and account types (zero-filled), overlapping
/// multi-account users, and the fixed 12 Cairo-month registration series.
/// Uses isolated synthetic data only - no Production/shared database.
/// </summary>
public sealed class AdminUserStatisticsTests
{
    private static readonly DateTime FrozenUtc = new(2026, 10, 12, 14, 0, 0, DateTimeKind.Utc);

    private static readonly string[] ExpectedMonths =
    [
        "2025-11", "2025-12",
        "2026-01", "2026-02", "2026-03", "2026-04",
        "2026-05", "2026-06", "2026-07", "2026-08", "2026-09", "2026-10",
    ];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Aggregates_DistinctUsers_StatusAndAccountCounts(bool sqlite)
    {
        await using IdentityDbContext db = await CreateDbAsync(sqlite);
        SeedMixedUsers(db);

        AdminUserStatisticsResponse response =
            (await new AdminUserStatisticsQueryHandler(db, new FixedClock(FrozenUtc))
                .Handle(new AdminUserStatisticsQuery(), default)).Value;

        Assert.Equal(8, response.TotalUsers);
        Assert.Equal(
            new[]
            {
                new UserStatusCount(UserStatus.PendingVerification, 2),
                new UserStatusCount(UserStatus.Active, 4),
                new UserStatusCount(UserStatus.Suspended, 1),
                new UserStatusCount(UserStatus.Blocked, 1),
            },
            response.UsersByStatus);
        Assert.Equal(
            new[]
            {
                new AccountTypeCount(AccountType.Family, 4),
                new AccountTypeCount(AccountType.MedicalCaregiver, 1),
                new AccountTypeCount(AccountType.CompanionCaregiver, 0),
                new AccountTypeCount(AccountType.Elderly, 1),
                new AccountTypeCount(AccountType.SuperAdmin, 1),
                new AccountTypeCount(AccountType.ContentAdmin, 0),
                new AccountTypeCount(AccountType.SupportAdmin, 0),
                new AccountTypeCount(AccountType.CareHomeOwner, 1),
                new AccountTypeCount(AccountType.FinanceAdmin, 0),
            },
            response.UsersByAccountType);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Series_ZeroFilled_CairoMonthBoundaries_CurrentPartialMonth(bool sqlite)
    {
        await using IdentityDbContext db = await CreateDbAsync(sqlite);
        // 2026-10-11T23:30Z and 2026-10-12T01:30Z are both inside the
        // current Cairo month; 2025-11-01 is the oldest window month.
        await SeedUserAsync(db, "2026-10-11T23:30:00Z");
        await SeedUserAsync(db, "2026-10-12T01:30:00Z");
        await SeedUserAsync(db, "2025-11-01T10:00:00Z");

        AdminUserStatisticsResponse response =
            (await new AdminUserStatisticsQueryHandler(db, new FixedClock(FrozenUtc))
                .Handle(new AdminUserStatisticsQuery(), default)).Value;

        Assert.Equal(12, response.NewRegistrationsByMonth.Count);
        Assert.Equal(ExpectedMonths, response.NewRegistrationsByMonth.Select(b => b.Month).ToArray());
        Assert.Equal(1, response.NewRegistrationsByMonth[0].Count);
        Assert.Equal(0, response.NewRegistrationsByMonth[1].Count);
        Assert.Equal(0, response.NewRegistrationsByMonth[10].Count);
        Assert.Equal(2, response.NewRegistrationsByMonth[11].Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Series_ZeroUsers_ReturnsAllZeroCounts(bool sqlite)
    {
        await using IdentityDbContext db = await CreateDbAsync(sqlite);

        AdminUserStatisticsResponse response =
            (await new AdminUserStatisticsQueryHandler(db, new FixedClock(FrozenUtc))
                .Handle(new AdminUserStatisticsQuery(), default)).Value;

        Assert.Equal(0, response.TotalUsers);
        Assert.All(response.UsersByStatus, c => Assert.Equal(0, c.Count));
        Assert.All(response.UsersByAccountType, c => Assert.Equal(0, c.DistinctUserCount));
        Assert.Equal(12, response.NewRegistrationsByMonth.Count);
        Assert.All(response.NewRegistrationsByMonth, bucket => Assert.Equal(0, bucket.Count));
        Assert.Equal(ExpectedMonths, response.NewRegistrationsByMonth.Select(b => b.Month).ToArray());
    }

    private static async Task<IdentityDbContext> CreateDbAsync(bool sqlite)
    {
        DbContextOptions<IdentityDbContext> options;
        SqliteConnection? connection = null;
        if (sqlite)
        {
            connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            options = new DbContextOptionsBuilder<IdentityDbContext>()
                .UseSqlite(connection).Options;
        }
        else
        {
            options = new DbContextOptionsBuilder<IdentityDbContext>()
                .UseInMemoryDatabase("admin-stats-" + Guid.NewGuid().ToString("N")).Options;
        }
        try
        {
            var db = new IdentityDbContext(options);
            if (sqlite)
                await db.Database.EnsureCreatedAsync();
            return db;
        }
        catch
        {
            if (connection is not null)
                await connection.DisposeAsync();
            throw;
        }
    }

    private static void SeedMixedUsers(IdentityDbContext db)
    {
        User pending1 = CreateUserWithStatus(db, "p1", user => { });
        User pending2 = CreateUserWithStatus(db, "p2", user => { });

        User active1 = CreateActive(db, "a1", AccountType.Family);
        User active2 = CreateActive(db, "a2", AccountType.SuperAdmin);
        User active3 = CreateActive(db, "a3", AccountType.CareHomeOwner);

        // Multi-account users: caregiver holds two overlapping account types;
        // suspended user also holds Elderly.
        User caregiver = CreateActive(db, "c1", AccountType.MedicalCaregiver);

        User suspended = CreateActive(db, "s1", AccountType.Elderly);
        suspended.Suspend("Review", FrozenUtc);

        User blocked = CreateActive(db, "b1", AccountType.Family);
        blocked.Block("Security", FrozenUtc);

        db.SaveChanges();
        Assert.Equal(UserStatus.PendingVerification, pending1.Status);
        Assert.Equal(UserStatus.Active, active1.Status);
        Assert.Equal(UserStatus.Active, active2.Status);
        Assert.Equal(UserStatus.Active, active3.Status);
        Assert.Equal(UserStatus.Active, caregiver.Status);
        Assert.Equal(UserStatus.Suspended, suspended.Status);
        Assert.Equal(UserStatus.Blocked, blocked.Status);
    }

    private static User CreateActive(IdentityDbContext db, string tag, AccountType type)
    {
        void Activate(User user)
        {
            user.SetInitialPasswordHash("test-hash", FrozenUtc);
            user.VerifyEmail(FrozenUtc);
            user.VerifyPhone(FrozenUtc);
            user.Activate(FrozenUtc);
        }

        return CreateUserWithStatus(db, tag, Activate, type);
    }

    private static int _phoneCounter;

    private static User CreateUserWithStatus(
        IdentityDbContext db,
        string tag,
        Action<User> configure,
        AccountType type = AccountType.Family)
    {
        User user = User.Create(
            FullName.Create("مستخدم " + tag),
            FullName.Create("User " + tag),
            Email.Create($"{tag}@example.test"),
            PhoneNumber.Create("+20" + Interlocked.Increment(ref _phoneCounter).ToString("D10")));
        user.AddAccount(type);
        configure(user);
        db.Users.Add(user);
        return user;
    }

    private static async Task SeedUserAsync(IdentityDbContext db, string createdOnUtc)
    {
        User user = User.Create(
            FullName.Create("نمو"),
            FullName.Create("Growth"),
            null,
            PhoneNumber.Create("+20" + (1000000000L + (long)(Guid.NewGuid().GetHashCode() & 0x7FFFFFFF) % 900000000L)));
        user.AddAccount(AccountType.Family);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        db.Entry(user).Property(nameof(User.CreatedOnUtc)).CurrentValue =
            DateTime.Parse(createdOnUtc, null, System.Globalization.DateTimeStyles.RoundtripKind);
        await db.SaveChangesAsync();
        await db.Entry(user).ReloadAsync();
    }

    private sealed class FixedClock(DateTime utcNow)
        : Sanad.BuildingBlocks.Application.Abstractions.IDateTimeProvider
    {
        public DateTime UtcNow { get; } = utcNow;
    }
}
