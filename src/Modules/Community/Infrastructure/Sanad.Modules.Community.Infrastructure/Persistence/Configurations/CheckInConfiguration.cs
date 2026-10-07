using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Community.Domain.CheckIns;

namespace Sanad.Modules.Community.Infrastructure.Persistence.Configurations;

public sealed class CheckInConfiguration : IEntityTypeConfiguration<CheckIn>
{
    public void Configure(EntityTypeBuilder<CheckIn> builder)
    {
        builder.ToTable("CheckIns", CommunityDbContext.Schema);
        builder.HasKey(checkIn => checkIn.Id);
        builder.Property(checkIn => checkIn.Id)
            .HasConversion(id => id.Value, value => new CommunityCheckInId(value));
        builder.Property(checkIn => checkIn.PostId)
            .HasConversion(id => id.Value, value => new CommunityPostId(value));
        builder.Property(checkIn => checkIn.UserId)
            .HasConversion(id => id.Value, value => new UserId(value));
        builder.Property(checkIn => checkIn.TimeZoneId).HasMaxLength(100);
        builder.Property(checkIn => checkIn.LocalDate).HasColumnType("date");
        builder.Property(checkIn => checkIn.AnsweredAtLocalTime).HasColumnType("time");
        builder.Property(checkIn => checkIn.AnsweredOnUtc)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasOne(checkIn => checkIn.Post)
            .WithMany()
            .HasForeignKey(checkIn => checkIn.PostId);
        builder.HasIndex(checkIn => new { checkIn.PostId, checkIn.UserId, checkIn.LocalDate })
            .IsUnique();
    }
}
