using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;

namespace Sanad.Modules.Identity.Infrastructure.Persistence.Configurations;

public sealed class DeviceSessionConfiguration :
    IEntityTypeConfiguration<DeviceSession>
{
    public void Configure(
        EntityTypeBuilder<DeviceSession> builder)
    {
        builder.ToTable("device_sessions");

        builder.HasKey(session =>
            session.Id);

        builder.Property(session => session.Id)
            .HasConversion(
                id => id.Value,
                value => new DeviceSessionId(value))
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(session =>
                session.UserId)
            .HasConversion(
                id => id.Value,
                value =>
                    new UserId(value))
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(session =>
                session.DeviceName)
            .HasColumnName("device_name")
            .HasMaxLength(
                DeviceSession
                    .MaximumDeviceNameLength)
            .IsRequired();

        builder.Property(session =>
                session.Platform)
            .HasColumnName("platform")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(session =>
                session.AppVersion)
            .HasColumnName("app_version")
            .HasMaxLength(
                DeviceSession
                    .MaximumAppVersionLength)
            .IsRequired();

        builder.Property(session =>
                session.RefreshTokenHash)
            .HasColumnName("refresh_token_hash")
            .HasMaxLength(
                DeviceSession
                    .MaximumTokenHashLength)
            .IsRequired();

        builder.Property(session =>
                session.RotationCount)
            .HasColumnName("rotation_count")
            .IsRequired();

        builder.Property(session =>
                session.CreatedOnUtc)
            .HasColumnName("created_on_utc")
            .IsRequired();

        builder.Property(session =>
                session.LastRotatedOnUtc)
            .HasColumnName("last_rotated_on_utc");

        builder.Property(session =>
                session.ExpiresOnUtc)
            .HasColumnName("expires_on_utc")
            .IsRequired();

        builder.Property(session =>
                session.RevokedOnUtc)
            .HasColumnName("revoked_on_utc");

        builder.Property(session =>
                session.RevocationReason)
            .HasColumnName("revocation_reason")
            .HasMaxLength(
                DeviceSession
                    .MaximumRevocationReasonLength);

        builder.Property(session =>
                session.ReuseDetectedOnUtc)
            .HasColumnName("reuse_detected_on_utc");

        builder.HasIndex(
            session => new
            {
                session.UserId,
                session.RevokedOnUtc,
                session.ExpiresOnUtc
            });

        builder.HasIndex(
            session => session.RefreshTokenHash);

        builder.HasIndex(
            session => session.ExpiresOnUtc);

        builder.Ignore(session =>
            session.IsRevoked);

        builder.Ignore(session =>
            session.HasReuseDetection);

        builder.Ignore(session =>
            session.DomainEvents);
    }
}