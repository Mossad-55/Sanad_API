using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Community.Domain.Posts;

namespace Sanad.Modules.Community.Infrastructure.Persistence.Configurations;

public class CommunityPostReportConfiguration : IEntityTypeConfiguration<CommunityPostReport>
{
    public void Configure(EntityTypeBuilder<CommunityPostReport> builder)
    {
        builder.ToTable("CommunityPostReports", CommunityDbContext.Schema);
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasConversion(id => id.Value, value => new CommunityPostReportId(value));
        builder.Property(e => e.PostId)
            .HasConversion(id => id.Value, value => new CommunityPostId(value))
            .IsRequired();
        builder.Property(e => e.ReporterId)
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();
        builder.Property(e => e.ReasonCode)
            .HasMaxLength(CommunityPostReport.MaximumReasonCodeLength)
            .IsRequired();
        builder.Property(e => e.Note)
            .HasMaxLength(CommunityPostReport.MaximumNoteLength)
            .IsRequired(false);
        builder.Property(e => e.Status)
            .HasConversion<int>()
            .IsRequired();
        builder.Property(e => e.Version)
            .IsConcurrencyToken()
            .IsRequired();
        builder.Property(e => e.CreatedOnUtc)
            .HasColumnType("timestamp with time zone");
        builder.Property(e => e.ResolvedOnUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);
        builder.Property(e => e.ResolvedBy)
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new UserId(value.Value) : (UserId?)null)
            .IsRequired(false);
        builder.Property(e => e.ResolutionNote)
            .HasMaxLength(CommunityPostReport.MaximumNoteLength)
            .IsRequired(false);

        // One retained report per reporter/post pair: a repeat request must
        // return the existing row rather than insert another.
        builder.HasIndex(e => new { e.PostId, e.ReporterId })
            .IsUnique();

        // Queue filter (?reportStatus=) and open-report counters.
        builder.HasIndex(e => e.Status);

        builder.HasOne<Post>()
            .WithMany()
            .HasForeignKey(e => e.PostId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
