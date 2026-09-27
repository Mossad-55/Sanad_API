using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Elderlies.CheckIns;

namespace Sanad.Modules.Families.Infrastructure.Persistence.Configurations;

public sealed class ElderlyCheckInConfiguration : IEntityTypeConfiguration<ElderlyCheckIn>
{
    public void Configure(EntityTypeBuilder<ElderlyCheckIn> builder)
    {
        builder.ToTable("elderly_check_ins");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.ElderlyId).HasConversion(x => x.Value, x => new ElderlyId(x)).HasColumnName("elderly_id").IsRequired();
        builder.HasOne<Elderly>().WithMany().HasForeignKey(x => x.ElderlyId).OnDelete(DeleteBehavior.Cascade);
        builder.Property(x => x.LocalDate).HasColumnName("local_date").IsRequired();
        builder.Property(x => x.Answer).HasColumnName("answer").IsRequired();
        builder.Property(x => x.AnsweredAtLocalTime).HasColumnName("answered_at_local_time").IsRequired();
        builder.Property(x => x.AnsweredOnUtc).HasColumnName("answered_on_utc").IsRequired();
        builder.Property(x => x.AnsweredByUserId).HasConversion(x => x.Value, x => new UserId(x)).HasColumnName("answered_by_user_id").IsRequired();
        builder.HasIndex(x => new { x.ElderlyId, x.LocalDate }).IsUnique();
        builder.HasIndex(x => x.LocalDate);
    }
}
