using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Domain.Sos;

namespace Sanad.Modules.Families.Infrastructure.Persistence.Configurations;

public sealed class ElderlySosConfiguration : IEntityTypeConfiguration<ElderlySos>
{
    public void Configure(EntityTypeBuilder<ElderlySos> b)
    {
        b.ToTable("elderly_sos"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.ElderlyIdentityUserId).HasConversion(x => x.Value, x => new UserId(x)).IsRequired();
        b.Property(x => x.IdempotencyKey).HasMaxLength(200).IsRequired(); b.Property(x => x.Status).HasConversion<int>().IsRequired(); b.Property(x => x.UpdatedOnUtc).IsConcurrencyToken();
        b.Property(x => x.Latitude).HasPrecision(9, 3); b.Property(x => x.Longitude).HasPrecision(9, 3);
        b.HasIndex(x => new { x.ElderlyIdentityUserId, x.IdempotencyKey }).IsUnique(); b.HasIndex(x => x.CreatedOnUtc);
        b.HasMany(x => x.History).WithOne().HasForeignKey(x => x.ElderlySosId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ElderlySosHistoryConfiguration : IEntityTypeConfiguration<ElderlySosHistory>
{
    public void Configure(EntityTypeBuilder<ElderlySosHistory> b)
    {
        b.ToTable("elderly_sos_history"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Action).HasConversion<int>(); b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.ActorUserId).HasConversion(x => x.Value, x => new UserId(x)); b.HasIndex(x => new { x.ElderlySosId, x.OccurredOnUtc });
    }
}
