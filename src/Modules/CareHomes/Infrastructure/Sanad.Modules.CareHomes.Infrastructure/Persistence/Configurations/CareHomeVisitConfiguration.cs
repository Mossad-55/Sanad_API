using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Domain.Bookings;
using Sanad.Modules.CareHomes.Domain.Facilities;

namespace Sanad.Modules.CareHomes.Infrastructure.Persistence.Configurations;

public sealed class CareHomeVisitSettingsConfiguration : IEntityTypeConfiguration<CareHomeVisitSettings>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<CareHomeVisitSettings> b)
    {
        b.ToTable("visit_settings");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.FacilityId).HasConversion(x => x.Value, x => new CareHomeId(x)).HasColumnName("facility_id").IsRequired();
        b.HasIndex(x => x.FacilityId).IsUnique();
        b.HasOne<CareHomeFacility>().WithMany().HasForeignKey(x => x.FacilityId).OnDelete(DeleteBehavior.Cascade);
        b.Property(x => x.VisitorCapacity).HasColumnName("visitor_capacity").IsRequired();
        b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        b.Property(x => x.UpdatedOnUtc).HasColumnName("updated_on_utc").IsRequired();
        ConfigureJson(b.Property(x => x.OperatingHours), "operating_hours_json");
        ConfigureJson(b.Property(x => x.VisitingWindows), "visiting_windows_json");
        ConfigureJson(b.Property(x => x.Closures), "closures_json");
    }

    private static void ConfigureJson<T>(PropertyBuilder<IReadOnlyList<T>> property, string column)
    {
        property.HasColumnName(column).HasColumnType("jsonb").HasConversion(
            value => JsonSerializer.Serialize(value, JsonOptions),
            value => JsonSerializer.Deserialize<List<T>>(value, JsonOptions) ?? new List<T>());
        property.Metadata.SetValueComparer(new ValueComparer<IReadOnlyList<T>>(
            (left, right) => left!.SequenceEqual(right!),
            value => value.Aggregate(0, (hash, item) => HashCode.Combine(hash, item)),
            value => value.ToArray()));
    }
}

public sealed class CareHomeVisitConfiguration : IEntityTypeConfiguration<CareHomeVisit>
{
    public void Configure(EntityTypeBuilder<CareHomeVisit> b)
    {
        b.ToTable("visits");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.FacilityId).HasConversion(x => x.Value, x => new CareHomeId(x)).HasColumnName("facility_id").IsRequired();
        b.Property(x => x.FamilyId).HasConversion(x => x.Value, x => new FamilyId(x)).HasColumnName("family_id").IsRequired();
        b.Property(x => x.FamilyUserId).HasConversion(x => x.Value, x => new UserId(x)).HasColumnName("family_user_id").IsRequired();
        b.Property(x => x.Kind).HasConversion<int>().HasColumnName("kind").IsRequired();
        b.Property(x => x.ElderlyId).HasConversion(x => x.HasValue ? x.Value.Value : (Guid?)null,
            x => x.HasValue ? new ElderlyId(x.Value) : (ElderlyId?)null).HasColumnName("elderly_id");
        b.Property(x => x.VisitorName).HasColumnName("visitor_name").HasMaxLength(200).IsRequired();
        b.Property(x => x.VisitorPhone).HasColumnName("visitor_phone").HasMaxLength(32).IsRequired();
        b.Property(x => x.VisitorCount).HasColumnName("visitor_count").IsRequired();
        b.Property(x => x.StartsAtUtc).HasColumnName("starts_at_utc").IsRequired();
        b.Property(x => x.EndsAtUtc).HasColumnName("ends_at_utc").IsRequired();
        b.Property(x => x.Status).HasConversion<int>().HasColumnName("status").IsRequired();
        b.Property(x => x.RescheduleOfVisitId).HasColumnName("reschedule_of_visit_id");
        b.HasOne<CareHomeVisit>().WithMany().HasForeignKey(x => x.RescheduleOfVisitId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.DecisionExpiresOnUtc).HasColumnName("decision_expires_on_utc").IsRequired();
        b.Property(x => x.DecidedByUserId).HasConversion(x => x.HasValue ? x.Value.Value : (Guid?)null,
            x => x.HasValue ? new UserId(x.Value) : (UserId?)null).HasColumnName("decided_by_user_id");
        b.Property(x => x.DecidedOnUtc).HasColumnName("decided_on_utc");
        b.Property(x => x.DecisionReason).HasColumnName("decision_reason").HasMaxLength(1000);
        b.Property(x => x.CancelledByUserId).HasConversion(x => x.HasValue ? x.Value.Value : (Guid?)null,
            x => x.HasValue ? new UserId(x.Value) : (UserId?)null).HasColumnName("cancelled_by_user_id");
        b.Property(x => x.CancelledOnUtc).HasColumnName("cancelled_on_utc");
        b.Property(x => x.CancellationReason).HasColumnName("cancellation_reason").HasMaxLength(1000);
        b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        b.Property(x => x.CreatedOnUtc).HasColumnName("created_on_utc").IsRequired();
        b.Property(x => x.UpdatedOnUtc).HasColumnName("updated_on_utc").IsRequired();
        b.HasOne<CareHomeFacility>().WithMany().HasForeignKey(x => x.FacilityId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.FacilityId, x.Status, x.StartsAtUtc });
        b.HasIndex(x => new { x.FamilyId, x.Status, x.StartsAtUtc });
        b.HasIndex(x => new { x.RescheduleOfVisitId, x.Status });
    }
}
