using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Identity.Application.Abstractions.Data;
using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;
using Sanad.Modules.Identity.Domain.Authentication.VerificationRequests;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.Modules.Identity.Infrastructure.Persistence.Challenges;

namespace Sanad.Modules.Identity.Infrastructure.Persistence;

public sealed class IdentityDbContext :
    DbContext,
    IIdentityDbContext
{
    public const string Schema = "identity";

    public IdentityDbContext(
        DbContextOptions<IdentityDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users =>
        Set<User>();

    public DbSet<VerificationRequest>
        VerificationRequests =>
            Set<VerificationRequest>();

    public DbSet<DeviceSession>
        DeviceSessions =>
            Set<DeviceSession>();

    internal DbSet<SocialAuthenticationChallengeRecord>
        SocialAuthenticationChallenges =>
            Set<SocialAuthenticationChallengeRecord>();

    internal DbSet<SocialRegistrationChallengeRecord>
        SocialRegistrationChallenges =>
            Set<SocialRegistrationChallengeRecord>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(
            Schema);

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(IdentityDbContext).Assembly);

        base.OnModelCreating(
            modelBuilder);
    }
}