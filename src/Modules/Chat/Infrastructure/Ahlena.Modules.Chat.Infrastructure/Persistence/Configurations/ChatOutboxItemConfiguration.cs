using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Chat.Domain.Conversations;
using Ahlena.Modules.Chat.Domain.Messages;
using Ahlena.Modules.Chat.Domain.Outbox;

namespace Ahlena.Modules.Chat.Infrastructure.Persistence.Configurations;

public class ChatOutboxItemConfiguration : IEntityTypeConfiguration<ChatOutboxItem>
{
    public void Configure(EntityTypeBuilder<ChatOutboxItem> builder)
    {
        builder.ToTable("ChatOutbox", ChatDbContext.Schema);
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasConversion(id => id.Value, value => new ChatOutboxItemId(value));
        builder.Property(e => e.ConversationId)
            .HasConversion(id => id.Value, value => new ConversationId(value));
        builder.Property(e => e.MessageId)
            .HasConversion(id => id.Value, value => new MessageId(value));
        builder.Property(e => e.RecipientUserId)
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();
        builder.Property(e => e.CreatedOnUtc)
            .HasColumnType("timestamp with time zone");
        builder.Property(e => e.PublishedOnUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);
        builder.Property(e => e.AttemptCount)
            .IsRequired();
        builder.Property(e => e.NextAttemptOnUtc)
            .HasColumnType("timestamp with time zone");
        builder.Property(e => e.ClaimToken)
            .IsRequired(false);
        builder.Property(e => e.ClaimedOnUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);
        builder.Property(e => e.LastErrorCode)
            .HasMaxLength(200)
            .IsRequired(false);
        builder.Ignore(e => e.DomainEvents);

        builder.HasOne<Conversation>()
            .WithMany()
            .HasForeignKey(e => e.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        // At most one intent per (message, recipient); the dispatcher
        // claims pending rows by (published-null, next-attempt, stale claim).
        builder.HasIndex(e => new { e.MessageId, e.RecipientUserId })
            .IsUnique();
        builder.HasIndex(e => new { e.PublishedOnUtc, e.NextAttemptOnUtc });
    }
}
