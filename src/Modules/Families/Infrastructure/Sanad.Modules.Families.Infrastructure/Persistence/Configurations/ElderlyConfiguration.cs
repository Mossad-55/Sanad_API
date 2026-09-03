using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Elderlies.Medical;

namespace Sanad.Modules.Families.Infrastructure.Persistence.Configurations;

public sealed class ElderlyConfiguration :
    IEntityTypeConfiguration<Elderly>
{
    public void Configure(EntityTypeBuilder<Elderly> builder)
    {
        builder.ToTable("elderlies");

        builder.HasKey(elderly => elderly.Id);

        builder.Property(elderly => elderly.Id)
            .HasConversion(
                id => id.Value,
                value => new ElderlyId(value))
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(elderly => elderly.OwnerUserId)
            .HasConversion(
                id => id.Value,
                value => new UserId(value))
            .HasColumnName("owner_user_id")
            .IsRequired();

        // The Identity user of the dependent. Unique index enforces the
        // one-elderly -> one-family rule at the database level.
        builder.Property(elderly => elderly.IdentityUserId)
            .HasConversion(
                id => id.Value,
                value => new UserId(value))
            .HasColumnName("identity_user_id")
            .IsRequired();

        builder.Property(elderly => elderly.FamilyId)
            .HasConversion(
                id => id.Value,
                value => new FamilyId(value))
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

        builder.Property(elderly => elderly.ProfileImageKey)
            .HasColumnName("profile_image_key")
            .HasMaxLength(Elderly.MaximumProfileImageKeyLength);

        builder.Property(elderly => elderly.DetailedAddress)
            .HasColumnName("detailed_address")
            .HasMaxLength(Elderly.MaximumDetailedAddressLength);

        builder.Property(elderly => elderly.HealthNotes)
            .HasColumnName("health_notes")
            .HasMaxLength(Elderly.MaximumHealthNotesLength);

        builder.Property(elderly => elderly.RelationshipType)
            .HasConversion<int>()
            .HasColumnName("relationship_type")
            .IsRequired();

        builder.Property(elderly => elderly.CreatedOnUtc)
            .HasColumnName("created_on_utc")
            .IsRequired();

        builder.Property(elderly => elderly.UpdatedOnUtc)
            .HasColumnName("updated_on_utc")
            .IsRequired();

        builder.HasIndex(elderly => elderly.IdentityUserId)
            .IsUnique();

        builder.HasIndex(elderly => elderly.FamilyId);

        builder.HasIndex(elderly => elderly.OwnerUserId);

        builder.OwnsOne(
            elderly => elderly.MedicalProfile,
            profile =>
            {
                profile.ToTable("elderly_medical_profiles");

                profile.WithOwner()
                    .HasForeignKey("ElderlyId");

                profile.Property<ElderlyId>("ElderlyId")
                    .HasConversion(
                        id => id.Value,
                        value => new ElderlyId(value))
                    .HasColumnName("elderly_id")
                    .IsRequired();

                profile.HasKey("ElderlyId");

                profile.Property(p => p.BloodType)
                    .HasColumnName("blood_type")
                    .HasConversion<int>()
                    .IsRequired();

                profile.Property(p => p.HeightCm)
                    .HasColumnName("height_cm");

                profile.Property(p => p.WeightKg)
                    .HasColumnName("weight_kg")
                    .HasPrecision(5, 1);

                profile.Property(p => p.UpdatedOnUtc)
                    .HasColumnName("updated_on_utc")
                    .IsRequired();

                var stringListComparer = new ValueComparer<IReadOnlyList<string>>(
                    (c1, c2) => (c1 == null && c2 == null) || (c1 != null && c2 != null && c1.SequenceEqual(c2)),
                    c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                    c => c.ToList().AsReadOnly());

                profile.Property(p => p.ChronicConditions)
                    .HasConversion(
                        v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                        v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>())
                    .Metadata.SetValueComparer(stringListComparer);

                profile.Property(p => p.ChronicConditions)
                    .HasColumnName("chronic_conditions")
                    .IsRequired();

                var allergyListComparer = new ValueComparer<IReadOnlyList<AllergyEntry>>(
                    (c1, c2) => (c1 == null && c2 == null) || (c1 != null && c2 != null && c1.SequenceEqual(c2)),
                    c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                    c => c.ToList().AsReadOnly());

                profile.Property(p => p.Allergies)
                    .HasConversion(
                        v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                        v => JsonSerializer.Deserialize<List<AllergyEntry>>(v, (JsonSerializerOptions?)null) ?? new List<AllergyEntry>())
                    .Metadata.SetValueComparer(allergyListComparer);

                profile.Property(p => p.Allergies)
                    .HasColumnName("allergies")
                    .IsRequired();

                var historyListComparer = new ValueComparer<IReadOnlyList<MedicalHistoryEntry>>(
                    (c1, c2) => (c1 == null && c2 == null) || (c1 != null && c2 != null && c1.SequenceEqual(c2)),
                    c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                    c => c.ToList().AsReadOnly());

                profile.Property(p => p.MedicalHistory)
                    .HasConversion(
                        v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                        v => JsonSerializer.Deserialize<List<MedicalHistoryEntry>>(v, (JsonSerializerOptions?)null) ?? new List<MedicalHistoryEntry>())
                    .Metadata.SetValueComparer(historyListComparer);

                profile.Property(p => p.MedicalHistory)
                    .HasColumnName("medical_history")
                    .IsRequired();
            });

        builder.Ignore(elderly => elderly.DomainEvents);
    }
}