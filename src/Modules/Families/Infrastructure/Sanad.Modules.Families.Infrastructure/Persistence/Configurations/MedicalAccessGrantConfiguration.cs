using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Domain.Elderlies;

namespace Sanad.Modules.Families.Infrastructure.Persistence.Configurations;

public sealed class MedicalAccessGrantConfiguration : IEntityTypeConfiguration<MedicalAccessGrant>
{
    public void Configure(EntityTypeBuilder<MedicalAccessGrant> builder)
    {
        builder.ToTable("MedicalAccessGrants", FamiliesDbContext.Schema);
        builder.HasKey(grant => grant.Id);
        builder.Property(grant => grant.Id)
            .HasConversion(id => id.Value, value => new MedicalAccessGrantId(value));
        builder.Property(grant => grant.DependentId)
            .HasConversion(id => id.Value, value => new ElderlyId(value));
        builder.Property(grant => grant.GrantedByUserId)
            .HasConversion(id => id.Value, value => new UserId(value));
        builder.Property(grant => grant.GranteeUserId)
            .HasConversion(id => id.Value, value => new UserId(value));
        builder.Property(grant => grant.RevokedByUserId)
            .HasConversion(id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new UserId(value.Value) : null);
        builder.Property(grant => grant.GrantType).HasConversion<int>();
        builder.Property(grant => grant.Notes).HasMaxLength(MedicalAccessGrant.MaximumNotesLength);
        builder.HasIndex(grant => new { grant.DependentId, grant.GranteeUserId });
        builder.Ignore(grant => grant.IsActive);
    }
}
