using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sanad.BuildingBlocks.Infrastructure.Time;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.Modules.Identity.Infrastructure.Persistence;
using Sanad.Modules.Identity.Infrastructure.Persistence.Seeding;
using Sanad.Modules.Identity.Infrastructure.Security;

namespace Sanad.UnitTests.Identity.Infrastructure;

public sealed class SuperAdminSeederTests
{
    [Fact]
    public async Task SeedAsync_ShouldDoNothing_WhenOptionsAreIncomplete()
    {
        await using IdentityDbContext dbContext =
            CreateDbContext();

        SuperAdminSeeder seeder =
            CreateSeeder(
                dbContext,
                new AdminSeedOptions());

        await seeder.SeedAsync();

        Assert.Empty(
            dbContext.Users);
    }

    [Fact]
    public async Task SeedAsync_ShouldCreateActiveSuperAdmin_WhenNoneExists()
    {
        await using IdentityDbContext dbContext =
            CreateDbContext();

        SuperAdminSeeder seeder =
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
            AccountType.SuperAdmin,
            Assert.Single(user.Accounts).AccountType);
    }

    [Fact]
    public async Task SeedAsync_ShouldNotCreateSecondSuperAdmin_WhenOneExists()
    {
        await using IdentityDbContext dbContext =
            CreateDbContext();

        AdminSeedOptions options =
            CreateConfiguredOptions();

        SuperAdminSeeder seeder =
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

        AdminSeedOptions options =
            CreateConfiguredOptions() with
            {
                Password = "short"
            };

        SuperAdminSeeder seeder =
            CreateSeeder(
                dbContext,
                options);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => seeder.SeedAsync());

        Assert.Empty(
            dbContext.Users);
    }

    private static AdminSeedOptions CreateConfiguredOptions()
    {
        return new AdminSeedOptions
        {
            ArabicFullName = "مدير سند",
            EnglishFullName = "Sanad Admin",
            Email = "admin@sanad.local",
            PhoneNumber = "+201001234567",
            Password = "AdminSeed1X"
        };
    }

    private static SuperAdminSeeder CreateSeeder(
        IdentityDbContext dbContext,
        AdminSeedOptions options)
    {
        return new SuperAdminSeeder(
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