using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.Modules.Cms.Domain.MedicationLateness;

namespace Sanad.Modules.Cms.Infrastructure.Persistence.Configurations;

public sealed class MedicationLatenessSettingConfiguration : IEntityTypeConfiguration<MedicationLatenessSetting>
{
    public void Configure(EntityTypeBuilder<MedicationLatenessSetting> b)
    {
        b.ToTable("medication_lateness_settings");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.HasMany(x => x.Revisions).WithOne(x => x.Setting).HasForeignKey(x => x.SettingId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class MedicationLatenessSettingRevisionConfiguration : IEntityTypeConfiguration<MedicationLatenessSettingRevision>
{
    public void Configure(EntityTypeBuilder<MedicationLatenessSettingRevision> b)
    {
        b.ToTable("medication_lateness_setting_revisions");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.ThresholdMinutes).IsRequired();
        b.Property(x => x.CreatedOnUtc).IsRequired();
        b.HasIndex(x => new { x.SettingId, x.Version }).IsUnique();
        b.HasIndex(x => new { x.SettingId, x.IsActive }).IsUnique().HasFilter("is_active = TRUE");
    }
}
