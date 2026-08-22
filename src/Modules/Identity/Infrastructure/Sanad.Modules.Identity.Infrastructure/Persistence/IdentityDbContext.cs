using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Identity.Application.Abstractions.Data;
using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;
using Sanad.Modules.Identity.Domain.Authentication.VerificationRequests;
using Sanad.Modules.Identity.Domain.Users;

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