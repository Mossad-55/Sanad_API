using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Chat.Domain.Attachments;
using Sanad.Modules.Chat.Domain.Conversations;

namespace Sanad.Modules.Chat.Infrastructure.Persistence.Configurations;

public class AttachmentConfiguration : IEntityTypeConfiguration<Attachment>
{
    public void Configure(EntityTypeBuilder<Attachment> builder)
    {
        builder.ToTable("Attachments", ChatDbContext.Schema);
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasConversion(id => id.Value, value => new AttachmentId(value));
        builder.Property(e => e.ConversationId)
            .HasConversion(id => id.Value, value => new ConversationId(value));
        builder.Property(e => e.UploadedBy)
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();
        builder.Property(e => e.StorageKey)
            .HasMaxLength(Attachment.MaximumStorageKeyLength)
            .IsRequired();
        builder.Property(e => e.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(e => e.SizeBytes).IsRequired();
        builder.Property(e => e.UploadedOnUtc)
            .HasColumnType("timestamp with time zone");
        builder.Property(e => e.IsBound).IsRequired();
        builder.Ignore(e => e.DomainEvents);
        builder.HasOne<Conversation>()
            .WithMany()
            .HasForeignKey(e => e.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => e.StorageKey).IsUnique();
    }
}
