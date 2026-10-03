using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Domain.Facilities;

namespace Sanad.Modules.CareHomes.Infrastructure.Persistence.Configurations;

public sealed class CareHomeFacilityConfiguration : IEntityTypeConfiguration<CareHomeFacility>
{
    public void Configure(EntityTypeBuilder<CareHomeFacility> builder)
    {
        builder.ToTable("facilities");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasConversion(x => x.Value, value => new CareHomeId(value))
            .HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.OwnerUserId)
            .HasConversion(x => x.Value, value => new UserId(value))
            .HasColumnName("owner_user_id").IsRequired();
        builder.HasIndex(x => x.OwnerUserId).IsUnique();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<int>().IsRequired();
        builder.Property(x => x.SubmittedRevisionId).HasColumnName("submitted_revision_id");
        builder.Property(x => x.ApprovedRevisionId).HasColumnName("approved_revision_id");
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        builder.Property(x => x.CreatedOnUtc).HasColumnName("created_on_utc").IsRequired();
        builder.Property(x => x.UpdatedOnUtc).HasColumnName("updated_on_utc").IsRequired();

        builder.HasMany(x => x.Revisions).WithOne().HasForeignKey("care_home_id").OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Revisions).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(x => x.Documents).WithOne().HasForeignKey("care_home_id").OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Documents).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(x => x.ReviewHistory).WithOne().HasForeignKey("care_home_id").OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.ReviewHistory).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(x => x.DomainEvents);
    }
}

public sealed class CareHomeProfileRevisionConfiguration : IEntityTypeConfiguration<CareHomeProfileRevision>
{
    public void Configure(EntityTypeBuilder<CareHomeProfileRevision> builder)
    {
        builder.ToTable("profile_revisions");
        ConfigureFacilityForeignKey(builder);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.RevisionNumber).HasColumnName("revision_number").IsRequired();
        builder.Property(x => x.CreatedByUserId).HasConversion(x => x.Value, value => new UserId(value)).HasColumnName("created_by_user_id").IsRequired();
        builder.Property(x => x.CreatedOnUtc).HasColumnName("created_on_utc").IsRequired();
        builder.Property(x => x.UpdatedOnUtc).HasColumnName("updated_on_utc").IsRequired();
        builder.Property(x => x.SubmittedOnUtc).HasColumnName("submitted_on_utc");
        builder.Property(x => x.ApprovedOnUtc).HasColumnName("approved_on_utc");

        ConfigureText(builder.Property(x => x.ArabicName), "arabic_name", 200);
        ConfigureText(builder.Property(x => x.EnglishName), "english_name", 200);
        ConfigureText(builder.Property(x => x.ArabicDescription), "arabic_description", 4000);
        ConfigureText(builder.Property(x => x.EnglishDescription), "english_description", 4000);
        ConfigureText(builder.Property(x => x.ContactName), "contact_name", 200);
        ConfigureText(builder.Property(x => x.ContactPhone), "contact_phone", 32);
        ConfigureText(builder.Property(x => x.ContactEmail), "contact_email", 256, required: false);
        ConfigureText(builder.Property(x => x.Governorate), "governorate", 200);
        ConfigureText(builder.Property(x => x.City), "city", 200);
        ConfigureText(builder.Property(x => x.Area), "area", 200);
        ConfigureText(builder.Property(x => x.Address), "address", 1000);
        ConfigureText(builder.Property(x => x.ArabicAdmissionConditions), "arabic_admission_conditions", 4000);
        ConfigureText(builder.Property(x => x.EnglishAdmissionConditions), "english_admission_conditions", 4000);
        ConfigureJson(builder.Property(x => x.Amenities), "amenities_json");
        ConfigureJson(builder.Property(x => x.MedicalServices), "medical_services_json");
        builder.HasIndex("care_home_id", nameof(CareHomeProfileRevision.RevisionNumber)).IsUnique();
    }

    private static void ConfigureText<TProperty>(PropertyBuilder<TProperty> property, string name, int maxLength, bool required = true)
    {
        property.HasColumnName(name).HasMaxLength(maxLength).IsRequired(required);
    }

    private static void ConfigureJson(PropertyBuilder<IReadOnlyList<BilingualCareHomeItem>> property, string name)
    {
        property.HasColumnName(name).HasConversion(
                value => JsonSerializer.Serialize(value, (JsonSerializerOptions?)null),
                value => JsonSerializer.Deserialize<List<BilingualCareHomeItem>>(value, (JsonSerializerOptions?)null) ?? new List<BilingualCareHomeItem>());
        property.Metadata.SetValueComparer(new ValueComparer<IReadOnlyList<BilingualCareHomeItem>>(
            (left, right) => left!.SequenceEqual(right!),
            value => value.Aggregate(0, (hash, item) => HashCode.Combine(hash, item)),
            value => value.ToArray()));
    }

    internal static void ConfigureFacilityForeignKey<TEntity>(EntityTypeBuilder<TEntity> builder)
        where TEntity : class => builder.Property<CareHomeId>("care_home_id")
        .HasConversion(value => value.Value, value => new CareHomeId(value))
        .IsRequired();
}

public sealed class CareHomeDocumentConfiguration : IEntityTypeConfiguration<CareHomeDocument>
{
    public void Configure(EntityTypeBuilder<CareHomeDocument> builder)
    {
        builder.ToTable("documents");
        CareHomeProfileRevisionConfiguration.ConfigureFacilityForeignKey(builder);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.Type).HasColumnName("type").HasConversion<int>().IsRequired();
        builder.Property(x => x.ProfileRevisionId).HasColumnName("profile_revision_id").IsRequired();
        builder.Property(x => x.PrivateStorageKey).HasColumnName("private_storage_key").HasMaxLength(500).IsRequired();
        builder.Property(x => x.ContentType).HasColumnName("content_type").HasMaxLength(100).IsRequired();
        builder.Property(x => x.Length).HasColumnName("length").IsRequired();
        builder.Property(x => x.ExpiryDate).HasColumnName("expiry_date");
        builder.Property(x => x.VerifiedNonExpiring).HasColumnName("verified_non_expiring").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<int>().IsRequired();
        builder.Property(x => x.VerifiedByUserId)
            .HasConversion(x => x.HasValue ? x.Value.Value : (Guid?)null,
                value => value.HasValue ? new UserId(value.Value) : null)
            .HasColumnName("verified_by_user_id");
        builder.Property(x => x.VerifiedOnUtc).HasColumnName("verified_on_utc");
        builder.Property(x => x.ReviewReason).HasColumnName("review_reason").HasMaxLength(1000);
        builder.Property(x => x.CreatedOnUtc).HasColumnName("created_on_utc").IsRequired();
        builder.Property(x => x.UpdatedOnUtc).HasColumnName("updated_on_utc").IsRequired();
        builder.HasIndex("care_home_id", nameof(CareHomeDocument.ProfileRevisionId), nameof(CareHomeDocument.Type));
    }
}

public sealed class CareHomeReviewHistoryConfiguration : IEntityTypeConfiguration<CareHomeReviewHistory>
{
    public void Configure(EntityTypeBuilder<CareHomeReviewHistory> builder)
    {
        builder.ToTable("review_history");
        CareHomeProfileRevisionConfiguration.ConfigureFacilityForeignKey(builder);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.ProfileRevisionId).HasColumnName("profile_revision_id");
        builder.Property(x => x.Action).HasColumnName("action").HasConversion<int>().IsRequired();
        builder.Property(x => x.PreviousStatus).HasColumnName("previous_status").HasConversion<int>().IsRequired();
        builder.Property(x => x.NewStatus).HasColumnName("new_status").HasConversion<int>().IsRequired();
        builder.Property(x => x.ActorUserId).HasConversion(x => x.Value, value => new UserId(value)).HasColumnName("actor_user_id").IsRequired();
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(1000);
        builder.Property(x => x.OccurredOnUtc).HasColumnName("occurred_on_utc").IsRequired();
    }
}

internal static class CareHomeInventoryConfiguration
{
    internal static void ConfigureFacility<TEntity>(EntityTypeBuilder<TEntity> builder) where TEntity : class => builder.Property<CareHomeId>("FacilityId").HasConversion(x => x.Value, x => new CareHomeId(x)).HasColumnName("care_home_id").IsRequired();
}

public sealed class CareHomeRoomTypeConfiguration : IEntityTypeConfiguration<CareHomeRoomType>
{
    public void Configure(EntityTypeBuilder<CareHomeRoomType> b)
    { b.ToTable("room_types"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever(); CareHomeInventoryConfiguration.ConfigureFacility(b); b.HasOne<CareHomeFacility>().WithMany().HasForeignKey(nameof(CareHomeRoomType.FacilityId)).OnDelete(DeleteBehavior.Cascade); b.Property(x => x.ArabicName).HasMaxLength(200).IsRequired(); b.Property(x => x.EnglishName).HasMaxLength(200).IsRequired(); b.Property(x => x.ArabicDescription).HasMaxLength(4000); b.Property(x => x.EnglishDescription).HasMaxLength(4000); b.Property(x => x.MonthlyPriceEgp).HasPrecision(18, 2).IsRequired(); b.Property(x => x.AllocationMode).HasConversion<int>().IsRequired(); b.Property(x => x.IsArchived).IsRequired(); b.Property(x => x.CreatedOnUtc).IsRequired(); b.Property(x => x.UpdatedOnUtc).IsRequired(); b.HasIndex(nameof(CareHomeRoomType.FacilityId), nameof(CareHomeRoomType.EnglishName)); }
}

public sealed class CareHomeRoomConfiguration : IEntityTypeConfiguration<CareHomeRoom>
{
    public void Configure(EntityTypeBuilder<CareHomeRoom> b)
    { b.ToTable("rooms"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever(); CareHomeInventoryConfiguration.ConfigureFacility(b); b.HasOne<CareHomeFacility>().WithMany().HasForeignKey(nameof(CareHomeRoom.FacilityId)).OnDelete(DeleteBehavior.Cascade); b.HasOne<CareHomeRoomType>().WithMany().HasForeignKey(nameof(CareHomeRoom.RoomTypeId)).OnDelete(DeleteBehavior.Restrict); b.Property(x => x.RoomTypeId).HasColumnName("room_type_id").IsRequired(); b.Property(x => x.RoomNumber).HasMaxLength(64).IsRequired(); b.Property(x => x.IsArchived).IsRequired(); b.Property(x => x.CreatedOnUtc).IsRequired(); b.Property(x => x.UpdatedOnUtc).IsRequired(); b.HasIndex(nameof(CareHomeRoom.FacilityId), nameof(CareHomeRoom.RoomNumber)).IsUnique().HasDatabaseName("ux_care_homes_rooms_facility_room_number"); }
}

public sealed class CareHomeBedConfiguration : IEntityTypeConfiguration<CareHomeBed>
{
    public void Configure(EntityTypeBuilder<CareHomeBed> b)
    { b.ToTable("beds"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever(); CareHomeInventoryConfiguration.ConfigureFacility(b); b.HasOne<CareHomeFacility>().WithMany().HasForeignKey(nameof(CareHomeBed.FacilityId)).OnDelete(DeleteBehavior.Cascade); b.HasOne<CareHomeRoom>().WithMany().HasForeignKey(nameof(CareHomeBed.RoomId)).OnDelete(DeleteBehavior.Cascade); b.Property(x => x.RoomId).HasColumnName("room_id").IsRequired(); b.Property(x => x.Label).HasMaxLength(64).IsRequired(); b.Property(x => x.IsArchived).IsRequired(); b.Property(x => x.CreatedOnUtc).IsRequired(); b.Property(x => x.UpdatedOnUtc).IsRequired(); b.HasIndex(x => new { x.RoomId, x.Label }).IsUnique().HasDatabaseName("ux_care_homes_beds_room_label"); }
}

public sealed class CareHomeMaintenanceBlockConfiguration : IEntityTypeConfiguration<CareHomeMaintenanceBlock>
{
    public void Configure(EntityTypeBuilder<CareHomeMaintenanceBlock> b)
    { b.ToTable("maintenance_blocks"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever(); CareHomeInventoryConfiguration.ConfigureFacility(b); b.HasOne<CareHomeFacility>().WithMany().HasForeignKey(nameof(CareHomeMaintenanceBlock.FacilityId)).OnDelete(DeleteBehavior.Cascade); b.Property(x => x.Target).HasConversion<int>().IsRequired(); b.Property(x => x.TargetId).HasColumnName("target_id").IsRequired(); b.Property(x => x.StartDate).HasColumnName("start_date").IsRequired(); b.Property(x => x.EndDate).HasColumnName("end_date").IsRequired(); b.Property(x => x.Reason).HasMaxLength(1000); b.Property(x => x.CreatedOnUtc).IsRequired(); b.HasIndex(nameof(CareHomeMaintenanceBlock.FacilityId), nameof(CareHomeMaintenanceBlock.Target), nameof(CareHomeMaintenanceBlock.TargetId)); }
}
