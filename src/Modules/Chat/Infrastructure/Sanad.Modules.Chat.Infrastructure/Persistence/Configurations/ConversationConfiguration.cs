using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Chat.Domain.Conversations;

namespace Sanad.Modules.Chat.Infrastructure.Persistence.Configurations;

public class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.ToTable("Conversations", ChatDbContext.Schema);
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasConversion(id => id.Value, value => new ConversationId(value));
        builder.Property(e => e.ParticipantOne)
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();
        builder.Property(e => e.ParticipantTwo)
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();
        builder.Property(e => e.Scope).HasConversion<int>();
        builder.Property(e => e.BookingId)
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue
                    ? new BookingId(value.Value)
                    : (BookingId?)null)
            .IsRequired(false);
        builder.Property(e => e.HiddenForParticipantOne).IsRequired();
        builder.Property(e => e.HiddenForParticipantTwo).IsRequired();
        builder.Property(e => e.CreatedOnUtc)
            .HasColumnType("timestamp with time zone");
        builder.Property(e => e.LastActivityOnUtc)
            .HasColumnType("timestamp with time zone");
        // Chat Plus commit-safe cursor: gapless per-conversation revision
        // assigned in the writer's transaction. Concurrency token so
        // concurrent writers collide and retry instead of duplicating a
        // revision.
        builder.Property(e => e.ChangeRevision)
            .IsConcurrencyToken()
            .IsRequired();
        builder.Ignore(e => e.DomainEvents);

        // Open-or-get idempotency: one row per normalized pair per booking
        // reference, and at most one booking-less (direct) row per pair.
        // PostgreSQL unique indexes treat NULLs as distinct, so a single
        // unique (pair, scope, booking) index would not dedupe direct
        // conversations (BookingId NULL); two NULL-filtered unique indexes
        // close both scopes. Scope is implied by the filter (Direct <=> NULL,
        // Booking <=> NOT NULL) but kept in the columns for parity with the
        // domain key.
        builder.HasIndex(e => new
        {
            e.ParticipantOne,
            e.ParticipantTwo,
            e.Scope,
            e.BookingId
        })
            .IsUnique()
            .HasDatabaseName("IX_Conversations_ParticipantOne_ParticipantTwo_Scope_BookingId")
            .HasFilter("\"BookingId\" IS NOT NULL");
        builder.HasIndex(e => new
        {
            e.ParticipantOne,
            e.ParticipantTwo,
            e.Scope
        })
            .IsUnique()
            .HasDatabaseName("IX_Conversations_ParticipantOne_ParticipantTwo_Scope_Direct")
            .HasFilter("\"BookingId\" IS NULL");
        builder.HasIndex(e => e.LastActivityOnUtc)
            .IsDescending(true);
    }
}
