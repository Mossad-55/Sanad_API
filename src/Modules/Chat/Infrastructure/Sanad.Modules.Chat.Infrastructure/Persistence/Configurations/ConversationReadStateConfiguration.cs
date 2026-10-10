using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Chat.Domain.Conversations;

namespace Sanad.Modules.Chat.Infrastructure.Persistence.Configurations;

public class ConversationReadStateConfiguration
    : IEntityTypeConfiguration<ConversationReadState>
{
    public void Configure(EntityTypeBuilder<ConversationReadState> builder)
    {
        builder.ToTable("ConversationReadStates", ChatDbContext.Schema);
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasConversion(id => id.Value, value => new ConversationReadStateId(value));
        builder.Property(e => e.ConversationId)
            .HasConversion(id => id.Value, value => new ConversationId(value));
        builder.Property(e => e.UserId)
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();
        builder.Property(e => e.LastReadMessageId).IsRequired();
        builder.Property(e => e.ReadOnUtc)
            .HasColumnType("timestamp with time zone");
        builder.Ignore(e => e.DomainEvents);

        // Deleting a conversation takes its read marks with it.
        builder.HasOne<Conversation>()
            .WithMany()
            .HasForeignKey(e => e.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        // Idempotency: exactly one mark per participant per conversation, so
        // a repeated PUT updates that row instead of inserting a second one.
        builder.HasIndex(e => new { e.ConversationId, e.UserId })
            .IsUnique();
    }
}
