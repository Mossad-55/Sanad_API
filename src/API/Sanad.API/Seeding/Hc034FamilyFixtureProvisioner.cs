using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Infrastructure.Persistence;
using Sanad.Modules.Identity.Application.Abstractions.Security;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.Modules.Identity.Infrastructure.Persistence;

namespace Sanad.API.Seeding;

public static class Hc034FamilyFixtureProvisioner
{
    public const string Command = "--provision-hc034-family-fixture";
    private const string ApprovedDatabase = "SanadBrunoTestDb";
    private const int ApprovedPort = 5432;
    private const string ApprovedFixtureEmail = "hc034.family.other@test.sanad.local";

    public static bool IsRequested(string[] args) =>
        args.Any(x => string.Equals(x, Command, StringComparison.Ordinal));

    public static void ValidateTarget(IConfiguration configuration, IHostEnvironment environment)
    {
        if (!environment.IsDevelopment())
            throw new InvalidOperationException("The HC-034 Family fixture is available only in Development.");

        string identity = Required(configuration, "IdentityDatabase");
        string[] names = ["IdentityDatabase", "CmsDatabase", "CaregiversDatabase", "FamiliesDatabase", "CareHomesDatabase", "NotificationsDatabase", "FinanceDatabase"];
        foreach (string name in names)
        {
            string connection = configuration.GetConnectionString(name) ?? identity;
            NpgsqlConnectionStringBuilder parsed;
            try { parsed = new(connection); }
            catch (ArgumentException) { throw UnsafeTarget(); }
            if (!IsLocalHost(parsed.Host) || parsed.Port != ApprovedPort || !string.Equals(parsed.Database, ApprovedDatabase, StringComparison.Ordinal))
                throw UnsafeTarget();
        }
    }

    public static async Task ProvisionAsync(IServiceProvider services, IConfiguration configuration, CancellationToken cancellationToken = default)
    {
        Hc034FamilyFixtureOptions options = configuration.GetSection(Hc034FamilyFixtureOptions.SectionName).Get<Hc034FamilyFixtureOptions>() ?? new();
        if (!options.Enabled)
            throw new InvalidOperationException($"{Hc034FamilyFixtureOptions.SectionName}:Enabled must be true for the one-shot provisioner.");
        if (string.IsNullOrWhiteSpace(options.Password))
            throw new InvalidOperationException($"{Hc034FamilyFixtureOptions.SectionName}:Password is required.");

        Email fixtureEmail = Email.Create(options.Email);
        if (options.ResetExistingPassword && !string.Equals(fixtureEmail.Value, ApprovedFixtureEmail, StringComparison.Ordinal))
            throw new InvalidOperationException("ResetExistingPassword is allowed only for the approved HC-034 fixture email.");

        IdentityDbContext identity = services.GetRequiredService<IdentityDbContext>();
        FamiliesDbContext families = services.GetRequiredService<FamiliesDbContext>();
        IPasswordHasher hasher = services.GetRequiredService<IPasswordHasher>();
        IDateTimeProvider clock = services.GetRequiredService<IDateTimeProvider>();
        DateTime utcNow = clock.UtcNow;

        User? user = await identity.Users.SingleOrDefaultAsync(x => x.Email == fixtureEmail, cancellationToken);
        if (user is null)
        {
            if (options.ResetExistingPassword)
                throw new InvalidOperationException("ResetExistingPassword requires the existing HC-034 Family fixture; it will not create an identity.");

            user = User.Create(FullName.Create("HC-034 Other Family"), FullName.Create("HC-034 Other Family"), Email.Create(options.Email), PhoneNumber.Create(options.PhoneNumber));
            user.AddAccount(AccountType.Family);
            user.VerifyEmail(utcNow);
            user.VerifyPhone(utcNow);
            user.SetInitialPasswordHash(hasher.Hash(options.Password), utcNow);
            user.Activate(utcNow);
            identity.Users.Add(user);
            await identity.SaveChangesAsync(cancellationToken);
        }
        else if (user.Status != UserStatus.Active || !user.Accounts.Any(x => x.AccountType == AccountType.Family))
        {
            throw new InvalidOperationException("The configured HC-034 fixture identity exists but is not an active Family account.");
        }

        Family? family = await families.Families.SingleOrDefaultAsync(x => x.OwnerUserId == user.Id && x.DeletedOnUtc == null, cancellationToken);
        if (options.ResetExistingPassword)
        {
            if (family is null)
                throw new InvalidOperationException("ResetExistingPassword requires the existing HC-034 Family aggregate; it will not create one.");

            user.ResetPasswordHash(hasher.Hash(options.Password), utcNow);
            DeviceSession[] activeSessions = await identity.DeviceSessions
                .Where(x => x.UserId == user.Id && x.RevokedOnUtc == null)
                .ToArrayAsync(cancellationToken);
            foreach (DeviceSession session in activeSessions)
                session.Revoke("Password was reset.", utcNow);

            await identity.SaveChangesAsync(cancellationToken);
            return;
        }

        if (family is null)
        {
            families.Families.Add(Family.Create(user.Id, options.FamilyName));
            await families.SaveChangesAsync(cancellationToken);
        }
    }

    private static string Required(IConfiguration configuration, string name) =>
        configuration.GetConnectionString(name) ?? throw new InvalidOperationException($"ConnectionStrings:{name} is required for the HC-034 fixture provisioner.");

    private static bool IsLocalHost(string? host) => string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) || string.Equals(host, "127.0.0.1", StringComparison.Ordinal) || string.Equals(host, "::1", StringComparison.Ordinal);

    private static InvalidOperationException UnsafeTarget() => new("HC-034 Family fixture provisioning is allowed only when every effective module connection targets localhost:5432/SanadBrunoTestDb.");
}
