using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.Modules.Identity.Domain.Support;

namespace Sanad.Modules.Identity.Infrastructure.Persistence.Configurations;

public sealed class PartnerContactConfiguration :
    IEntityTypeConfiguration<PartnerContact>
{
    public void Configure(
        EntityTypeBuilder<PartnerContact> builder)
    {
        builder.ToTable("partner_contacts");

        builder.HasKey(contact => contact.Id);

        builder.Property(contact => contact.Id)
            .HasConversion(
                id => id.Value,
                value => new PartnerContactId(value))
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(contact => contact.Name)
            .HasColumnName("name")
            .HasMaxLength(PartnerContact.MaximumNameLength)
            .IsRequired();

        builder.Property(contact => contact.Company)
            .HasColumnName("company")
            .HasMaxLength(PartnerContact.MaximumCompanyLength);

        builder.Property(contact => contact.Email)
            .HasColumnName("email")
            .HasMaxLength(PartnerContact.MaximumEmailLength)
            .IsRequired();

        builder.Property(contact => contact.Subject)
            .HasColumnName("subject")
            .HasMaxLength(PartnerContact.MaximumSubjectLength);

        builder.Property(contact => contact.Message)
            .HasColumnName("message")
            .HasMaxLength(PartnerContact.MaximumMessageLength)
            .IsRequired();

        builder.Property(contact => contact.IdempotencyKey)
            .HasColumnName("idempotency_key")
            .HasMaxLength(PartnerContact.MaximumIdempotencyKeyLength)
            .IsRequired();

        builder.Property(contact => contact.ClientKey)
            .HasColumnName("client_key")
            .HasMaxLength(PartnerContact.MaximumClientKeyLength)
            .IsRequired();

        builder.Property(contact => contact.CreatedOnUtc)
            .HasColumnName("created_on_utc")
            .IsRequired();

        // The idempotency lookup and the anti-spam window both read the
        // caller-scoped rows.
        builder.HasIndex(contact =>
            new { contact.ClientKey, contact.IdempotencyKey })
            .IsUnique();

        builder.HasIndex(contact =>
            new { contact.ClientKey, contact.CreatedOnUtc });

        builder.Ignore(contact => contact.DomainEvents);
    }
}
