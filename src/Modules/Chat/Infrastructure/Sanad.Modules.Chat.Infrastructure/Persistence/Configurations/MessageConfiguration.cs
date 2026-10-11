using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Chat.Domain.Attachments;
using Sanad.Modules.Chat.Domain.Conversations;
using Sanad.Modules.Chat.Domain.Messages;

namespace Sanad.Modules.Chat.Infrastructure.Persistence.Configurations;

public class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.ToTable("Messages", ChatDbContext.Schema);
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasConversion(id => id.Value, value => new MessageId(value));
        builder.Property(e => e.ConversationId)
            .HasConversion(id => id.Value, value => new ConversationId(value));
        builder.Property(e => e.SenderUserId)
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();
        builder.Property(e => e.Type)
            .HasConversion<int>()
            .IsRequired();
        // Nullable: File messages carry no body (see Message.Text).
        builder.Property(e => e.Text)
            .HasMaxLength(Message.MaximumTextLength)
            .IsRequired(false);
        // Nullable: a Text message has no attachment (see Message.AttachmentId).
        builder.Property(e => e.AttachmentId)
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue
                    ? new AttachmentId(value.Value)
                    : (AttachmentId?)null)
            .IsRequired(false);
        builder.Property(e => e.ClientMessageId)
            .HasMaxLength(Message.MaximumClientMessageIdLength)
            .IsRequired();
        builder.Property(e => e.SentOnUtc)
            .HasColumnType("timestamp with time zone");
        builder.Property(e => e.DeliveredOnUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);
        builder.Property(e => e.ReadOnUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);
        builder.Ignore(e => e.DomainEvents);

        // Deleting a conversation takes its messages with it. Nothing else
        // does: messages are immutable and have no delete path in V1.
        builder.HasOne<Conversation>()
            .WithMany()
            .HasForeignKey(e => e.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        // Idempotency backstop for endpoint 4: one message per client
        // reference per conversation, so a concurrent duplicate insert fails
        // at the database rather than silently creating a second message.
        builder.HasIndex(e => new { e.ConversationId, e.ClientMessageId })
            .IsUnique();

        // Cursor page of endpoint 3: keyset order (SentOnUtc desc, Id desc)
        // restricted to one conversation, oldest pages last.
        builder.HasIndex(e => new { e.ConversationId, e.SentOnUtc, e.Id })
            .IsDescending(false, true, true);

        // Sender-scoped scans: unread counting only ever asks about messages
        // the requester did not send, per conversation.
        builder.HasIndex(e => new { e.ConversationId, e.SenderUserId });
    }
}
