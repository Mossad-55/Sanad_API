using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Domain.Bookings;
using Sanad.Modules.CareHomes.Domain.Facilities;

namespace Sanad.Modules.CareHomes.Infrastructure.Persistence.Configurations;

public sealed class CareHomeCheckInDisputeConfiguration : IEntityTypeConfiguration<CareHomeCheckInDispute>
{
    public void Configure(EntityTypeBuilder<CareHomeCheckInDispute> b)
    {
        b.ToTable("check_in_disputes"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.FacilityId).HasConversion(x => x.Value, x => new CareHomeId(x)).IsRequired();
        b.Property(x => x.OpenedBy).HasConversion(x => x.Value, x => new UserId(x)).IsRequired();
        b.Property(x => x.ResolvedBy).HasConversion(x => x.HasValue ? x.Value.Value : (Guid?)null, x => x.HasValue ? new UserId(x.Value) : (UserId?)null);
        b.Property(x => x.Status).HasConversion<int>().IsRequired();
        b.Property(x => x.Evidence).HasMaxLength(4000); b.Property(x => x.Reason).HasMaxLength(2000); b.Property(x => x.FamilyReason).HasMaxLength(2000);
        b.HasIndex(x => new { x.BookingId, x.Status }).IsUnique().HasFilter("\"Status\" = 1");
    }
}
