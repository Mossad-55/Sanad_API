using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Families.Domain.Elderlies;

namespace Sanad.Modules.Families.Infrastructure.Persistence.Configurations;

public sealed class ElderlyConfiguration :
    IEntityTypeConfiguration<Elderly>
{
    public void Configure(EntityTypeBuilder<Elderly> builder)
    {
        builder.ToTable("elderlies");

        builder.HasKey(elderly => elderly.Id);

        builder.Property(elderly => elderly.Id)
            .HasConversion(id => id.Value, value => new ElderlyId(value))
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(elderly => elderly.OwnerUserId)
            .HasConversion(id => id.Value, value => new UserId(value))
            .HasColumnName("owner_user_id")
            .IsRequired();

        builder.Property(elderly => elderly.FamilyId)
            .HasConversion(id => id.Value, value => new FamilyId(value))
            .HasColumnName("family_id")
            .IsRequired();

        builder.Property(elderly => elderly.ArabicFullName)
            .HasConversion(
                value => value.Value,
                value => FullName.Create(value))
            .HasMaxLength(200)
            .HasColumnName("arabic_full_name")
            .IsRequired();

        builder.Property(elderly => elderly.EnglishFullName)
            .HasConversion(
                value => value.Value,
                value => FullName.Create(value))
            .HasMaxLength(200)
            .HasColumnName("english_full_name")
            .IsRequired();

        builder.Property(elderly => elderly.Gender)
            .HasConversion<int>()
            .HasColumnName("gender")
            .IsRequired();

        builder.Property(elderly => elderly.DateOfBirth)
            .HasColumnName("date_of_birth")
            .IsRequired();

        builder.Property(elderly => elderly.ProfileImageUrl)
            .HasColumnName("profile_image_url")
            .HasMaxLength(500);

        builder.Property(elderly => elderly.DetailedAddress)
            .HasColumnName("detailed_address")
            .HasMaxLength(Elderly.MaximumDetailedAddressLength);

        builder.Property(elderly => elderly.HealthNotes)
            .HasColumnName("health_notes")
            .HasMaxLength(Elderly.MaximumHealthNotesLength);

        builder.Property(elderly => elderly.CreatedOnUtc)
            .HasColumnName("created_on_utc")
            .IsRequired();

        builder.Property(elderly => elderly.UpdatedOnUtc)
            .HasColumnName("updated_on_utc")
            .IsRequired();

        builder.HasIndex(elderly => elderly.FamilyId);
        // owner_user_id uniqueness is enforced together with the Identity
        // one-elderly-per-family rule (a family owner has at most one family).

        builder.Ignore(elderly => elderly.DomainEvents);
    }
}