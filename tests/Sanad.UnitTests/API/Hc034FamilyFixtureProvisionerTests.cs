using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sanad.API.Seeding;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Infrastructure.Persistence;
using Sanad.Modules.Identity.Application.Abstractions.Security;
using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.Modules.Identity.Infrastructure.Persistence;
using Sanad.Modules.Identity.Infrastructure.Security;

namespace Sanad.UnitTests.API;

public sealed class Hc034FamilyFixtureProvisionerTests
{
    private const string FixtureEmail = "hc034.family.other@test.sanad.local";

    [Fact]
    public async Task Reset_existing_fixture_password_revokes_active_sessions_without_creating_family_data()
    {
        DateTime now = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);
        string database = Guid.NewGuid().ToString("N");
        await using IdentityDbContext identity = CreateIdentityContext(database);
        await using FamiliesDbContext families = CreateFamiliesContext(database);
        AspNetPasswordHasher hasher = new();
        User user = CreateActiveFamilyUser(hasher, now);
        identity.Users.Add(user);
        Family family = Family.Create(user.Id, "Existing fixture family");
        families.Families.Add(family);
        await identity.SaveChangesAsync();
        await families.SaveChangesAsync();

        DeviceSession session = DeviceSession.Create(
            user.Id,
            "existing-fixture-session",
            DevicePlatform.Android,
            "1.0",
            "existing-refresh-token-hash",
            now,
            now.AddDays(1));
        identity.DeviceSessions.Add(session);
        await identity.SaveChangesAsync();

        using ServiceProvider services = CreateServices(identity, families, hasher, now);
        IConfiguration configuration = CreateConfiguration(resetExistingPassword: true, FixtureEmail, "new-process-only-password");

        await Hc034FamilyFixtureProvisioner.ProvisionAsync(services, configuration);

        User persistedUser = await identity.Users.SingleAsync();
        DeviceSession persistedSession = await identity.DeviceSessions.SingleAsync();
        Assert.True(hasher.Verify(persistedUser.Password!.PasswordHash, "new-process-only-password") != PasswordVerificationResult.Failed);
        Assert.True(persistedSession.IsRevoked);
        Assert.Equal("Password was reset.", persistedSession.RevocationReason);
        Assert.Equal(1, await families.Families.CountAsync());
    }

    [Fact]
    public async Task Reset_missing_fixture_fails_without_creating_an_identity()
    {
        DateTime now = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);
        string database = Guid.NewGuid().ToString("N");
        await using IdentityDbContext identity = CreateIdentityContext(database);
        await using FamiliesDbContext families = CreateFamiliesContext(database);
        using ServiceProvider services = CreateServices(identity, families, new AspNetPasswordHasher(), now);
        IConfiguration configuration = CreateConfiguration(resetExistingPassword: true, FixtureEmail, "new-process-only-password");

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Hc034FamilyFixtureProvisioner.ProvisionAsync(services, configuration));

        Assert.Contains("will not create an identity", exception.Message, StringComparison.Ordinal);
        Assert.Empty(await identity.Users.ToArrayAsync());
        Assert.Empty(await families.Families.ToArrayAsync());
    }

    [Fact]
    public async Task Reset_option_rejects_any_email_other_than_the_approved_fixture()
    {
        DateTime now = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);
        string database = Guid.NewGuid().ToString("N");
        await using IdentityDbContext identity = CreateIdentityContext(database);
        await using FamiliesDbContext families = CreateFamiliesContext(database);
        using ServiceProvider services = CreateServices(identity, families, new AspNetPasswordHasher(), now);
        IConfiguration configuration = CreateConfiguration(resetExistingPassword: true, "another-family@test.sanad.local", "new-process-only-password");

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Hc034FamilyFixtureProvisioner.ProvisionAsync(services, configuration));

        Assert.Contains("approved HC-034 fixture email", exception.Message, StringComparison.Ordinal);
        Assert.Empty(await identity.Users.ToArrayAsync());
    }

    private static IdentityDbContext CreateIdentityContext(string database) =>
        new(new DbContextOptionsBuilder<IdentityDbContext>().UseInMemoryDatabase(database).Options);

    private static FamiliesDbContext CreateFamiliesContext(string database) =>
        new(new DbContextOptionsBuilder<FamiliesDbContext>().UseInMemoryDatabase(database).Options);

    private static User CreateActiveFamilyUser(AspNetPasswordHasher hasher, DateTime now)
    {
        User user = User.Create(
            FullName.Create("HC-034 Test Family"),
            FullName.Create("HC-034 Test Family"),
            Email.Create(FixtureEmail),
            PhoneNumber.Create("+201000000034"));
        user.AddAccount(AccountType.Family);
        user.VerifyEmail(now);
        user.VerifyPhone(now);
        user.SetInitialPasswordHash(hasher.Hash("old-fixture-password"), now);
        user.Activate(now);
        return user;
    }

    private static ServiceProvider CreateServices(
        IdentityDbContext identity,
        FamiliesDbContext families,
        IPasswordHasher hasher,
        DateTime now) =>
        new ServiceCollection()
            .AddSingleton(identity)
            .AddSingleton(families)
            .AddSingleton(hasher)
            .AddSingleton<IDateTimeProvider>(new FixedDateTimeProvider(now))
            .BuildServiceProvider();

    private static IConfiguration CreateConfiguration(bool resetExistingPassword, string email, string password) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{Hc034FamilyFixtureOptions.SectionName}:Enabled"] = "true",
                [$"{Hc034FamilyFixtureOptions.SectionName}:ResetExistingPassword"] = resetExistingPassword.ToString(),
                [$"{Hc034FamilyFixtureOptions.SectionName}:Email"] = email,
                [$"{Hc034FamilyFixtureOptions.SectionName}:Password"] = password
            })
            .Build();

    private sealed class FixedDateTimeProvider(DateTime utcNow) : IDateTimeProvider
    {
        public DateTime UtcNow { get; } = utcNow;
    }
}
