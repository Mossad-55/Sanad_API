using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.Modules.Cms.Domain.Help;

namespace Sanad.Modules.Cms.Infrastructure.Persistence.Configurations;

public sealed class SupportContactConfiguration :
    IEntityTypeConfiguration<SupportContact>
{
    public void Configure(
        EntityTypeBuilder<SupportContact> builder)
    {
        builder.ToTable(
            "support_contacts",
            table => table.HasCheckConstraint(
                "ck_support_contacts_single_row",
                "\"id\" = 1"));

        builder.HasKey(contact => contact.Id);

        // The domain always uses SupportContact.SingletonId, and the check
        // constraint guarantees the table can never hold a second row.
        builder.Property(contact => contact.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        // Either contact value may be unset independently: the columns are
        // nullable and an unset field is simply absent, never an empty string.
        builder.Property(contact => contact.SupportPhone)
            .HasColumnName("support_phone")
            .HasMaxLength(SupportContact.MaximumPhoneLength);

        builder.Property(contact => contact.SupportEmail)
            .HasColumnName("support_email")
            .HasMaxLength(SupportContact.MaximumEmailLength);

        builder.Property(contact => contact.CreatedOnUtc)
            .HasColumnName("created_on_utc")
            .IsRequired();

        builder.Property(contact => contact.UpdatedOnUtc)
            .HasColumnName("updated_on_utc")
            .IsRequired();
    }
}
