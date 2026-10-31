using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Chat.Domain.Attachments;
using Ahlena.Modules.Chat.Domain.Conversations;

namespace Ahlena.Modules.Chat.Infrastructure.Persistence.Configurations;

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
        // Bind-vs-cleanup exclusion: the send's change-tracking UPDATE only
        // matches while the row is still unbound, so a racing orphan sweep
        // that claimed (locked) the row first makes this UPDATE affect 0 rows
        // and surface DbUpdateConcurrencyException instead of silently leaving
        // a message bound to a deleted blob. InMemory ignores the token (no
        // race to prove there); PostgreSQL enforces it.
        builder.Property(e => e.IsBound)
            .IsConcurrencyToken()
            .IsRequired();
        builder.Ignore(e => e.DomainEvents);
        builder.HasOne<Conversation>()
            .WithMany()
            .HasForeignKey(e => e.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => e.StorageKey).IsUnique();

        // Orphan-sweep claim query: (IsBound, UploadedOnUtc) with the
        // StorageKey/SizeBytes payload covered by the row.
        builder.HasIndex(e => new { e.IsBound, e.UploadedOnUtc });
    }
}
