using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Community.Domain.Posts;

namespace Ahlena.Modules.Community.Infrastructure.Persistence.Configurations;

public sealed class PostViewConfiguration : IEntityTypeConfiguration<PostView>
{
    public void Configure(EntityTypeBuilder<PostView> builder)
    {
        builder.ToTable("PostViews", CommunityDbContext.Schema);
        builder.HasKey(view => view.Id);
        builder.Property(view => view.Id)
            .HasConversion(id => id.Value, value => new CommunityPostViewId(value));
        builder.Property(view => view.PostId)
            .HasConversion(id => id.Value, value => new CommunityPostId(value));
        builder.Property(view => view.ViewerUserId)
            .HasConversion(id => id.Value, value => new UserId(value));
        builder.Property(view => view.ViewedOnUtc)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        // The lifetime dedup rule: one viewer row per post, ever. The unique
        // index is also the concurrency backstop for two simultaneous first
        // views by the same caller.
        builder.HasIndex(view => new { view.PostId, view.ViewerUserId }).IsUnique();

        builder.HasOne<Post>()
            .WithMany()
            .HasForeignKey(view => view.PostId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
