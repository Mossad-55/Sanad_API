using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Identity.Domain.Support;
using Ahlena.Modules.Identity.Domain.Users;

namespace Ahlena.Modules.Identity.Infrastructure.Persistence.Configurations;

public sealed class SupportTicketReplyConfiguration :
    IEntityTypeConfiguration<SupportTicketReply>
{
    public void Configure(
        EntityTypeBuilder<SupportTicketReply> builder)
    {
        builder.ToTable("support_ticket_replies");

        builder.HasKey(reply => reply.Id);

        builder.Property(reply => reply.Id)
            .HasConversion(
                id => id.Value,
                value => new SupportTicketReplyId(value))
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(reply => reply.TicketId)
            .HasConversion(
                id => id.Value,
                value => new SupportTicketId(value))
            .HasColumnName("ticket_id")
            .IsRequired();

        builder.Property(reply => reply.ActorUserId)
            .HasConversion(
                id => id.Value,
                value => new UserId(value))
            .HasColumnName("actor_user_id")
            .IsRequired();

        builder.Property(reply => reply.Body)
            .HasColumnName("body")
            .HasMaxLength(SupportTicketReply.MaximumBodyLength)
            .IsRequired();

        builder.Property(reply => reply.CreatedOnUtc)
            .HasColumnName("created_on_utc")
            .IsRequired();

    }
}