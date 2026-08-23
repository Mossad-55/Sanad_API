using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Domain.Authentication.VerificationRequests;

namespace Sanad.Modules.Identity.Infrastructure.Persistence.Configurations;

public sealed class VerificationRequestConfiguration :
    IEntityTypeConfiguration<VerificationRequest>
{
    public void Configure(
        EntityTypeBuilder<VerificationRequest> builder)
    {
        builder.ToTable("verification_requests");

        builder.HasKey(request =>
            request.Id);

        builder.Property(request => request.Id)
            .HasConversion(
                id => id.Value,
                value => new VerificationRequestId(value))
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(request =>
                request.UserId)
            .HasConversion(
                id => id.HasValue
                    ? id.Value.Value
                    : (Guid?)null,
                value => value.HasValue
                    ? new UserId(
                        value.Value)
                    : (UserId?)null)
            .HasColumnName("user_id");

        builder.Property(request =>
                request.Target)
            .HasColumnName("target")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(request =>
                request.OtpHash)
            .HasColumnName("otp_hash")
            .HasMaxLength(
                VerificationRequest
                    .MaximumOtpHashLength)
            .IsRequired();

        builder.Property(request =>
                request.Channel)
            .HasColumnName("channel")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(request =>
                request.Purpose)
            .HasColumnName("purpose")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(request =>
                request.Status)
            .HasColumnName("status")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(request =>
                request.Attempts)
            .HasColumnName("attempts")
            .IsRequired();

        builder.Property(request =>
                request.MaxAttempts)
            .HasColumnName("max_attempts")
            .IsRequired();

        builder.Property(request =>
                request.CreatedOnUtc)
            .HasColumnName("created_on_utc")
            .IsRequired();

        builder.Property(request =>
                request.ExpiresOnUtc)
            .HasColumnName("expires_on_utc")
            .IsRequired();

        builder.Property(request =>
                request.VerifiedOnUtc)
            .HasColumnName("verified_on_utc");

        builder.Property(request =>
                request.InvalidatedOnUtc)
            .HasColumnName("invalidated_on_utc");

        builder.HasIndex(
            request => new
            {
                request.UserId,
                request.Purpose,
                request.Status
            });

        builder.HasIndex(
            request => new
            {
                request.Target,
                request.Purpose,
                request.Status
            });

        builder.HasIndex(
            request => request.ExpiresOnUtc);

        builder.Ignore(request =>
            request.DomainEvents);
    }
}