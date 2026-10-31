using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Chat.Domain.Changes;
using Ahlena.Modules.Chat.Domain.Conversations;
using Ahlena.Modules.Chat.Domain.Messages;

namespace Ahlena.Modules.Chat.Infrastructure.Persistence.Configurations;

public class ChatChangeConfiguration : IEntityTypeConfiguration<ChatChange>
{
    public void Configure(EntityTypeBuilder<ChatChange> builder)
    {
        builder.ToTable("ChatChanges", ChatDbContext.Schema);
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasConversion(id => id.Value, value => new ChatChangeId(value));
        builder.Property(e => e.ConversationId)
            .HasConversion(id => id.Value, value => new ConversationId(value));
        builder.Property(e => e.Revision)
            .IsRequired();
        builder.Property(e => e.Type)
            .HasConversion<int>()
            .IsRequired();
        builder.Property(e => e.MessageId)
            .HasConversion(id => id.Value, value => new MessageId(value));
        builder.Property(e => e.CreatedOnUtc)
            .HasColumnType("timestamp with time zone");
        builder.Property(e => e.PublishedOnUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);
        builder.Ignore(e => e.DomainEvents);

        builder.HasOne<Conversation>()
            .WithMany()
            .HasForeignKey(e => e.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Message>()
            .WithMany()
            .HasForeignKey(e => e.MessageId)
            .OnDelete(DeleteBehavior.Cascade);

        // Sync replay order and the gapless-cursor backstop: one row per
        // (conversation, revision).
        builder.HasIndex(e => new { e.ConversationId, e.Revision })
            .IsUnique();
        builder.HasIndex(e => new { e.ConversationId, e.MessageId, e.Type });
        // Dispatcher scan: unpublished changes first.
        builder.HasIndex(e => new { e.PublishedOnUtc, e.ConversationId, e.Revision });
    }
}
