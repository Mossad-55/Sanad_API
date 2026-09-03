using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Domain.Activities;

namespace Sanad.Modules.Families.Infrastructure.Persistence.Configurations;

public sealed class ElderlyActivityLogConfiguration : IEntityTypeConfiguration<ElderlyActivityLog>
{
    public void Configure(EntityTypeBuilder<ElderlyActivityLog> builder)
    {
        builder.ToTable("elderly_activity_logs");

        builder.HasKey(l => l.Id);

        builder.Property(l => l.Id)
            .HasConversion(id => id.Value, value => new ElderlyActivityLogId(value))
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(l => l.ElderlyId)
            .HasConversion(id => id.Value, value => new ElderlyId(value))
            .HasColumnName("elderly_id")
            .IsRequired();

        builder.Property(l => l.ActorUserId)
            .HasConversion(id => id.Value, value => new UserId(value))
            .HasColumnName("actor_user_id")
            .IsRequired();

        builder.Property(l => l.ActivityType)
            .HasConversion<int>()
            .HasColumnName("activity_type")
            .IsRequired();

        builder.Property(l => l.Summary)
            .HasMaxLength(ElderlyActivityLog.MaximumSummaryLength)
            .HasColumnName("summary")
            .IsRequired();

        builder.Property(l => l.CreatedOnUtc)
            .HasColumnName("created_on_utc")
            .IsRequired();

        builder.HasIndex(l => new { l.ElderlyId, l.CreatedOnUtc });
    }
}