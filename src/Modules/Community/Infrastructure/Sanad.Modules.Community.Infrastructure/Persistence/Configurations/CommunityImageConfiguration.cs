using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.Modules.Community.Domain.Uploads;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Community.Infrastructure.Persistence.Configurations;

public class CommunityImageConfiguration : IEntityTypeConfiguration<CommunityImage>
{
    public void Configure(EntityTypeBuilder<CommunityImage> builder)
    {
        builder.ToTable("CommunityImages", "community");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasConversion(id => id.Value, value => new CommunityImageId(value));
        builder.Property(e => e.StorageKey).HasMaxLength(CommunityImage.MaximumStorageKeyLength);
        builder.Property(e => e.ContentType).HasMaxLength(100);
        builder.Property(e => e.UploadedBy)
            .HasConversion(id => id.Value, value => new UserId(value));
        builder.Property(e => e.UploadedOnUtc)
            .HasColumnType("timestamp with time zone");
        builder.Ignore(e => e.DomainEvents);
    }
}
