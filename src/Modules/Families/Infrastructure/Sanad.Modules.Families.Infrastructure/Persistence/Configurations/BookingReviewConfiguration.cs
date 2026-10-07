using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Domain.Bookings;

namespace Sanad.Modules.Families.Infrastructure.Persistence.Configurations;

public sealed class BookingReviewConfiguration : IEntityTypeConfiguration<BookingReview>
{
    public void Configure(EntityTypeBuilder<BookingReview> builder)
    {
        builder.ToTable("BookingReviews", FamiliesDbContext.Schema);
        builder.HasKey(review => review.Id);
        builder.Property(review => review.Id)
            .HasConversion(id => id.Value, value => new BookingReviewId(value));
        builder.Property(review => review.BookingId)
            .HasConversion(id => id.Value, value => new BookingId(value));
        builder.Property(review => review.CreatedByUserId)
            .HasConversion(id => id.Value, value => new UserId(value));
        builder.Property(review => review.Comment)
            .HasMaxLength(BookingReview.MaximumCommentLength);
        builder.HasIndex(review => review.BookingId).IsUnique();
    }
}
