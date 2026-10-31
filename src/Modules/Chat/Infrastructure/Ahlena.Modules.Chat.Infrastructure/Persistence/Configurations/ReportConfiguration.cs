using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Chat.Domain.Conversations;
using Ahlena.Modules.Chat.Domain.Messages;
using Ahlena.Modules.Chat.Domain.Reports;

namespace Ahlena.Modules.Chat.Infrastructure.Persistence.Configurations;

public class ReportConfiguration : IEntityTypeConfiguration<Report>
{
    public void Configure(EntityTypeBuilder<Report> builder)
    {
        builder.ToTable("Reports", ChatDbContext.Schema);
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasConversion(id => id.Value, value => new ReportId(value));
        builder.Property(e => e.ConversationId)
            .HasConversion(id => id.Value, value => new ConversationId(value));
        // Nullable: a null target reports the conversation as a whole.
        builder.Property(e => e.MessageId)
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue
                    ? new MessageId(value.Value)
                    : (MessageId?)null)
            .IsRequired(false);
        builder.Property(e => e.ReporterUserId)
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();
        builder.Property(e => e.Reason)
            .HasMaxLength(Report.MaximumReasonLength)
            .IsRequired();
        builder.Property(e => e.Status)
            .HasConversion<int>()
            .IsRequired();
        // Chat Plus optimistic-resolution token: exactly one conditional
        // Open-to-Dismissed/Upheld transition wins; concurrent losers
        // collide here and surface the 409.
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
                value => value.HasValue
                    ? new UserId(value.Value)
                    : (UserId?)null)
            .IsRequired(false);
        builder.Ignore(e => e.DomainEvents);

        // Deleting a conversation takes its reports with it.
        builder.HasOne<Conversation>()
            .WithMany()
            .HasForeignKey(e => e.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        // The cited message is optional (conversation-level report) and uses
        // Restrict: a report is moderation evidence, so it must never be
        // silently re-targeted to null (SetNull) or erased with the message
        // (Cascade). V1 has no message delete path — messages are immutable —
        // so this only guards an out-of-band deletion, which should fail
        // loudly instead of dropping evidence.
        builder.HasOne<Message>()
            .WithMany()
            .HasForeignKey(e => e.MessageId)
            .OnDelete(DeleteBehavior.Restrict);

        // Support queue filter (endpoint 12: ?status=).
        builder.HasIndex(e => e.Status);

        // Reports are not unique per (conversation, reporter): repeating the
        // same report is a new row, so there is deliberately no unique index.
    }
}
