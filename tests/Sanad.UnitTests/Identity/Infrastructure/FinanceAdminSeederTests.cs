using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sanad.BuildingBlocks.Infrastructure.Time;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.Modules.Identity.Infrastructure.Persistence;
using Sanad.Modules.Identity.Infrastructure.Persistence.Seeding;
using Sanad.Modules.Identity.Infrastructure.Security;

namespace Sanad.UnitTests.Identity.Infrastructure;

public sealed class FinanceAdminSeederTests
{
    [Fact]
    public async Task SeedAsync_ShouldDoNothing_WhenOptionsAreIncomplete()
    {
        await using IdentityDbContext dbContext =
            CreateDbContext();

        FinanceAdminSeeder seeder =
            CreateSeeder(
                dbContext,
                new FinanceAdminSeedOptions());

        await seeder.SeedAsync();

        Assert.Empty(
            dbContext.Users);
    }

    [Fact]
    public async Task SeedAsync_ShouldCreateActiveFinanceAdmin_WhenNoneExists()
    {
        await using IdentityDbContext dbContext =
            CreateDbContext();

        FinanceAdminSeeder seeder =
            CreateSeeder(
                dbContext,
                CreateConfiguredOptions());

        await seeder.SeedAsync();

        User user =
            Assert.Single(
                dbContext.Users);

        Assert.Equal(
            UserStatus.Active,
            user.Status);

        Assert.True(
            user.EmailVerified);

        Assert.True(
            user.PhoneVerified);

        Assert.True(
            user.HasPassword);

        Assert.Equal(
            AccountType.FinanceAdmin,
            Assert.Single(user.Accounts).AccountType);
    }

    [Fact]
    public async Task SeedAsync_ShouldNotCreateSecondFinanceAdmin_WhenOneExists()
    {
        await using IdentityDbContext dbContext =
            CreateDbContext();

        FinanceAdminSeedOptions options =
            CreateConfiguredOptions();

        FinanceAdminSeeder seeder =
            CreateSeeder(
                dbContext,
                options);

        await seeder.SeedAsync();
        await seeder.SeedAsync();

        Assert.Single(
            dbContext.Users);
    }

    [Fact]
    public async Task SeedAsync_ShouldRejectWeakPassword_WhenConfigured()
    {
        await using IdentityDbContext dbContext =
            CreateDbContext();

        FinanceAdminSeedOptions options =
            CreateConfiguredOptions() with
            {
                Password = "short"
            };

        FinanceAdminSeeder seeder =
            CreateSeeder(
                dbContext,
                options);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => seeder.SeedAsync());

        Assert.Empty(
            dbContext.Users);
    }

    private static FinanceAdminSeedOptions CreateConfiguredOptions()
    {
        return new FinanceAdminSeedOptions
        {
            ArabicFullName = "المدير المالي",
            EnglishFullName = "Sanad Finance Admin",
            Email = "finance-admin@sanad.local",
            PhoneNumber = "+201001234568",
            Password = "FinanceSeed1X"
        };
    }

    private static FinanceAdminSeeder CreateSeeder(
        IdentityDbContext dbContext,
        FinanceAdminSeedOptions options)
    {
        return new FinanceAdminSeeder(
            dbContext,
            Options.Create(
                options),
            new AspNetPasswordHasher(),
            new SystemDateTimeProvider());
    }

    private static IdentityDbContext CreateDbContext()
    {
        DbContextOptions<IdentityDbContext> options =
            new DbContextOptionsBuilder<
                IdentityDbContext>()
                .UseInMemoryDatabase(
                    Guid.NewGuid().ToString())
                .Options;

        IdentityDbContext dbContext =
            new(options);

        dbContext.Database.EnsureCreated();

        return dbContext;
    }
}
