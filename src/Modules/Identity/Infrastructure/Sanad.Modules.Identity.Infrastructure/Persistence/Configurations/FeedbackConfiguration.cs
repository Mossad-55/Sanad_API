using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using FeedbackEntity = Sanad.Modules.Identity.Domain.Feedback;

namespace Sanad.Modules.Identity.Infrastructure.Persistence.Configurations;

public sealed class FeedbackConfiguration : IEntityTypeConfiguration<FeedbackEntity>
{
    public void Configure(EntityTypeBuilder<FeedbackEntity> builder)
    {
        builder.ToTable("Feedbacks", IdentityDbContext.Schema);
        builder.HasKey(feedback => feedback.Id);
        builder.Property(feedback => feedback.UserId)
            .HasConversion(userId => userId.Value, value => new UserId(value));
        builder.Property(feedback => feedback.Comment).HasColumnType("text");
        builder.Property(feedback => feedback.DeviceInfo).HasColumnType("text");
        builder.Property(feedback => feedback.AppVersion).HasColumnType("text");
        builder.Property(feedback => feedback.CreatedOnUtc)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasIndex(feedback => feedback.UserId);
    }
}
