using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Application.Abstractions.Data;
using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;
using Sanad.Modules.Identity.Domain.Authentication.VerificationRequests;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.Modules.Identity.Domain.Authentication.ExternalLogins;

namespace Sanad.UnitTests.Identity.Registration;

internal sealed class IdentityTestDbContext :
    DbContext,
    IIdentityDbContext
{
    internal IdentityTestDbContext(
        DbContextOptions<IdentityTestDbContext>
            options)
        : base(options)
    {
    }

    public DbSet<User> Users =>
        Set<User>();

    public DbSet<VerificationRequest>
        VerificationRequests =>
            Set<VerificationRequest>();

    public DbSet<DeviceSession> DeviceSessions =>
        Set<DeviceSession>();

    internal int SaveChangesCalls { get; private set; }

    public override async Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        SaveChangesCalls++;

        return await base.SaveChangesAsync(
            cancellationToken);
    }

    internal void ResetSaveChangesCalls()
    {
        SaveChangesCalls = 0;
    }

    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        ConfigureUser(modelBuilder);
        ConfigureVerificationRequest(modelBuilder);
        ConfigureDeviceSession(modelBuilder);
    }

    private static void ConfigureUser(
        ModelBuilder modelBuilder)
    {
        var user =
            modelBuilder.Entity<User>();

        user.HasKey(value =>
            value.Id);

        user.Property(value =>
                value.Id)
            .HasConversion(
                id => id.Value,
                value =>
                    new UserId(value));

        user.Property(value =>
                value.ArabicFullName)
            .HasConversion(
                name => name.Value,
                value =>
                    FullName.Create(value));

        user.Property(value =>
                value.EnglishFullName)
            .HasConversion(
                name => name.Value,
                value =>
                    FullName.Create(value));

        ValueConverter<Email?, string?>
            emailConverter =
                new(
                    email =>
                        email == null
                            ? null
                            : email.Value,
                    value =>
                        value == null
                            ? null
                            : Email.Create(value));

        user.Property(value =>
                value.Email)
            .HasConversion(
                emailConverter);

        user.Property(value =>
                value.PhoneNumber)
            .HasConversion(
                phone => phone.Value,
                value =>
                    PhoneNumber.Create(value));

        user.OwnsMany(
            value => value.ExternalLogins,
            externalLogin =>
            {
                externalLogin.WithOwner();

                externalLogin.HasKey(
                    value => value.Id);

                externalLogin.Property(
                        value => value.Id)
                    .HasConversion(
                        id => id.Value,
                        value =>
                            new UserExternalLoginId(
                                value));

                externalLogin.Property(
                        value => value.ProviderSubject)
                    .HasMaxLength(
                        UserExternalLogin
                            .MaximumProviderSubjectLength)
                    .IsRequired();

                externalLogin.Property(
                        value => value.Provider)
                    .IsRequired();

                externalLogin.Property(
                        value => value.LinkedOnUtc)
                    .IsRequired();
            });

        user.Ignore(value =>
            value.Password);

        user.Ignore(value =>
            value.HasPassword);

        user.Ignore(value =>
            value.IdentityDocument);

        user.Ignore(value =>
            value.Accounts);

        user.Ignore(value =>
            value.HasExternalLogin);

        user.Ignore(value =>
            value.DomainEvents);
    }

    private static void ConfigureVerificationRequest(
        ModelBuilder modelBuilder)
    {
        var request =
            modelBuilder
                .Entity<VerificationRequest>();

        request.HasKey(value =>
            value.Id);

        request.Property(value =>
                value.Id)
            .HasConversion(
                id => id.Value,
                value =>
                    new VerificationRequestId(
                        value));

        ValueConverter<UserId?, Guid?>
            nullableUserIdConverter =
                new(
                    id =>
                        id.HasValue
                            ? id.Value.Value
                            : null,
                    value =>
                        value.HasValue
                            ? new UserId(
                                value.Value)
                            : null);

        request.Property(value =>
                value.UserId)
            .HasConversion(
                nullableUserIdConverter);

        request.Ignore(value =>
            value.DomainEvents);
    }

    private static void ConfigureDeviceSession(
        ModelBuilder modelBuilder)
    {
        var session =
            modelBuilder.Entity<DeviceSession>();

        session.HasKey(value =>
            value.Id);

        session.Property(value =>
                value.Id)
            .HasConversion(
                id => id.Value,
                value =>
                    new DeviceSessionId(
                        value));

        session.Property(value =>
                value.UserId)
            .HasConversion(
                id => id.Value,
                value =>
                    new UserId(value));

        session.Ignore(value =>
            value.DomainEvents);
    }
}