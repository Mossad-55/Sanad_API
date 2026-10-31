using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Identity.Domain.Users;

namespace Ahlena.Modules.Identity.Infrastructure.Persistence.Configurations;

public sealed class AdminUserActionConfiguration : IEntityTypeConfiguration<AdminUserAction>
{
    public void Configure(EntityTypeBuilder<AdminUserAction> builder)
    {
        builder.ToTable("admin_user_actions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.ActorUserId).HasConversion(x => x.Value, x => new UserId(x)).HasColumnName("actor_user_id").IsRequired();
        builder.Property(x => x.TargetUserId).HasConversion(x => x == null ? (Guid?)null : x.Value.Value, x => x == null ? null : new UserId(x.Value)).HasColumnName("target_user_id");
        builder.Property(x => x.Action).HasConversion<int>().HasColumnName("action").IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(1000).HasColumnName("reason");
        builder.Property(x => x.CreatedOnUtc).HasColumnType("timestamp with time zone").HasColumnName("created_on_utc").IsRequired();
        builder.HasIndex(x => x.ActorUserId);
        builder.HasIndex(x => x.TargetUserId);
        builder.HasIndex(x => new { x.Action, x.CreatedOnUtc });
    }
}
