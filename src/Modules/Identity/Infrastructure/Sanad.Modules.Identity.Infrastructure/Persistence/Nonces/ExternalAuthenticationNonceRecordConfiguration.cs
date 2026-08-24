using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.Modules.Identity.Infrastructure.Persistence.Nonces;

namespace Sanad.Modules.Identity.Infrastructure.Persistence.Configurations;

internal sealed class ExternalAuthenticationNonceRecordConfiguration :
    IEntityTypeConfiguration<ExternalAuthenticationNonceRecord>
{
    public void Configure(
        EntityTypeBuilder<ExternalAuthenticationNonceRecord> builder)
    {
        builder.ToTable(
            "external_authentication_nonces");

        builder.HasKey(record =>
            record.Id);

        builder.Property(record =>
                record.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(record =>
                record.Provider)
            .HasColumnName("provider")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(record =>
                record.NonceHash)
            .HasColumnName("nonce_hash")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(record =>
                record.CreatedOnUtc)
            .HasColumnName("created_on_utc")
            .IsRequired();

        builder.Property(record =>
                record.ExpiresOnUtc)
            .HasColumnName("expires_on_utc")
            .IsRequired();

        builder.Property(record =>
                record.ConsumedOnUtc)
            .HasColumnName("consumed_on_utc");

        builder.HasIndex(record =>
                record.NonceHash)
            .IsUnique();

        builder.HasIndex(record =>
            new
            {
                record.Provider,
                record.ExpiresOnUtc,
                record.ConsumedOnUtc
            });
    }
}