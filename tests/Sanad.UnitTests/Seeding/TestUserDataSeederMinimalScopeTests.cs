using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Sanad.API.Seeding;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Infrastructure.Persistence;
using Sanad.Modules.Cms.Application.Abstractions.Data;
using Sanad.Modules.Cms.Infrastructure.Persistence;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Infrastructure.Persistence;
using Sanad.Modules.Identity.Application.Abstractions.Security;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.Modules.Identity.Infrastructure.Persistence;
using Sanad.Modules.Identity.Infrastructure.Security;

namespace Sanad.UnitTests.Seeding;

/// <summary>
/// L1 fixture-scope tests: <c>App:TestUserSeed:Scope=Minimal</c> seeds exactly
/// the two deterministic identities, stays idempotent across reruns, and never
/// widens the Full bundle's existing requirements. Everything runs against the
/// in-memory provider - no API and no database.
/// </summary>
public sealed class TestUserDataSeederMinimalScopeTests
{
    [Theory]
    [InlineData("sk_live-test-marker")]
    [InlineData("egy_sk_live_test-marker")]
    public async Task MinimalScope_RejectsLivePaymobKeyBeforeSeeding(string liveSecretKey)
    {
        await using Fixture fixture = await Fixture.CreateAsync(
            scope: "Minimal",
            elderlyPassword: null,
            paymobSecretKey: liveSecretKey);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Seeder.SeedAsync());

        Assert.Contains("LIVE Paymob key", exception.Message);
        Assert.Empty(await fixture.Identity.Users.ToListAsync());
    }

    [Fact]
    public async Task MinimalScope_SeedsExactlyTheTwoDeterministicIdentities()
    {
        // No ElderlyPassword, no RunId: Minimal must not require either.
        await using Fixture fixture = await Fixture.CreateAsync(
            scope: "Minimal",
            elderlyPassword: null);

        await fixture.Seeder.SeedAsync();

        List<User> users = await fixture.Identity.Users
            .AsNoTracking()
            .Include(u => u.Accounts)
            .ToListAsync();

        Assert.Equal(2, users.Count);
        Assert.Equal(
            new[] { "content.admin@test.sanad.local", "family.viewer@test.sanad.local" },
            users.Select(u => u.Email!.Value).OrderBy(email => email, StringComparer.Ordinal).ToArray());

        User admin = users.Single(u => u.Email!.Value == "content.admin@test.sanad.local");
        User viewer = users.Single(u => u.Email!.Value == "family.viewer@test.sanad.local");
        Assert.Contains(admin.Accounts, account => account.AccountType == AccountType.ContentAdmin);
        Assert.Contains(viewer.Accounts, account => account.AccountType == AccountType.Family);
        Assert.All(users, user => Assert.NotNull(user.Password));

        // Minimal never requires the Full bundle's extra configuration.
        Assert.Null(fixture.Configuration[$"{TestUserSeedOptions.SectionName}:ElderlyPassword"]);
        Assert.Null(fixture.Configuration[$"{TestUserSeedOptions.SectionName}:RunId"]);
    }

    [Fact]
    public async Task MinimalScope_Rerun_IsIdempotent_NoDuplicatesNoResets()
    {
        await using Fixture fixture = await Fixture.CreateAsync(scope: "Minimal");

        await fixture.Seeder.SeedAsync();
        UserId[] firstIds = (await fixture.Identity.Users.AsNoTracking().Select(u => u.Id).ToListAsync())
            .OrderBy(id => id.Value)
            .ToArray();
        string firstPasswordHash = (await fixture.Identity.Users.AsNoTracking().SingleAsync(
            u => u.Email!.Value == "content.admin@test.sanad.local")).Password!.PasswordHash;

        await fixture.Seeder.SeedAsync();

        UserId[] secondIds = (await fixture.Identity.Users.AsNoTracking().Select(u => u.Id).ToListAsync())
            .OrderBy(id => id.Value)
            .ToArray();
        string secondPasswordHash = (await fixture.Identity.Users.AsNoTracking().SingleAsync(
            u => u.Email!.Value == "content.admin@test.sanad.local")).Password!.PasswordHash;

        Assert.Equal(2, secondIds.Length);
        Assert.Equal(firstIds, secondIds);
        Assert.Equal(firstPasswordHash, secondPasswordHash);
    }

    [Fact]
    public async Task FullScope_WithoutElderlyPassword_KeepsTheExistingRequirement()
    {
        await using Fixture fixture = await Fixture.CreateAsync(scope: "Full", elderlyPassword: null);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Seeder.SeedAsync());

        Assert.Contains("ElderlyPassword", exception.Message, StringComparison.Ordinal);
        Assert.Empty(await fixture.Identity.Users.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task UnknownScope_FailsClosed()
    {
        await using Fixture fixture = await Fixture.CreateAsync(scope: "SomethingElse");

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Seeder.SeedAsync());

        Assert.Contains("must be 'Full' or 'Minimal'", exception.Message, StringComparison.Ordinal);
        Assert.Empty(await fixture.Identity.Users.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task DisabledSeed_NeverInspectsScope_AndSeedsNothing()
    {
        await using Fixture fixture = await Fixture.CreateAsync(
            scope: "SomethingElse",
            enabled: false);

        await fixture.Seeder.SeedAsync();

        Assert.Empty(await fixture.Identity.Users.AsNoTracking().ToListAsync());
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly IdentityDbContext _identity;
        private readonly FamiliesDbContext _families;
        private readonly CaregiversDbContext _caregivers;
        private readonly CmsDbContext _cms;

        private Fixture(
            IdentityDbContext identity,
            FamiliesDbContext families,
            CaregiversDbContext caregivers,
            CmsDbContext cms,
            TestUserDataSeeder seeder,
            IConfiguration configuration)
        {
            _identity = identity;
            _families = families;
            _caregivers = caregivers;
            _cms = cms;
            Seeder = seeder;
            Configuration = configuration;
        }

        public TestUserDataSeeder Seeder { get; }

        public IConfiguration Configuration { get; }

        public IdentityDbContext Identity => _identity;

        public static async Task<Fixture> CreateAsync(
            string scope,
            bool enabled = true,
            string? elderlyPassword = "Elderly-Test-1234!",
            string? paymobSecretKey = null)
        {
            string database = Guid.NewGuid().ToString("N");
            IdentityDbContext identity = new(
                new DbContextOptionsBuilder<IdentityDbContext>()
                    .UseInMemoryDatabase(database)
                    .Options);
            FamiliesDbContext families = new(
                new DbContextOptionsBuilder<FamiliesDbContext>()
                    .UseInMemoryDatabase(database)
                    .Options);
            CaregiversDbContext caregivers = new(
                new DbContextOptionsBuilder<CaregiversDbContext>()
                    .UseInMemoryDatabase(database)
                    .Options);
            CmsDbContext cms = new(
                new DbContextOptionsBuilder<CmsDbContext>()
                    .UseInMemoryDatabase(database)
                    .Options);

            Dictionary<string, string?> values = new()
            {
                [$"{TestUserSeedOptions.SectionName}:Enabled"] = enabled.ToString(),
                [$"{TestUserSeedOptions.SectionName}:Scope"] = scope
            };

            if (elderlyPassword is not null)
            {
                values[$"{TestUserSeedOptions.SectionName}:ElderlyPassword"] = elderlyPassword;
            }

            if (paymobSecretKey is not null)
            {
                values["Paymob:SecretKey"] = paymobSecretKey;
            }

            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(values)
                .Build();

            TestUserSeedOptions options = new()
            {
                Enabled = enabled,
                ElderlyPassword = elderlyPassword,
                Scope = scope
            };

            TestUserDataSeeder seeder = new(
                identity,
                families,
                caregivers,
                cms,
                new AspNetPasswordHasher(),
                new FixedDateTimeProvider(new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc)),
                Options.Create(options),
                configuration,
                new TestHostEnvironment(Environments.Development),
                platformChargeRuleSeeder: null!);

            // Ensure the InMemory store exists before any test assertion.
            await identity.Database.EnsureCreatedAsync();

            return new Fixture(identity, families, caregivers, cms, seeder, configuration);
        }

        public async ValueTask DisposeAsync()
        {
            await _identity.DisposeAsync();
            await _families.DisposeAsync();
            await _caregivers.DisposeAsync();
            await _cms.DisposeAsync();
        }
    }

    private sealed class FixedDateTimeProvider(DateTime utcNow) : IDateTimeProvider
    {
        public DateTime UtcNow { get; } = utcNow;
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Sanad.UnitTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
