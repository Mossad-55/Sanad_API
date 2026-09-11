using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Domain.Support;

namespace Sanad.Modules.Identity.Infrastructure.Persistence.Configurations;

public sealed class SupportTicketConfiguration :
    IEntityTypeConfiguration<SupportTicket>
{
    public void Configure(
        EntityTypeBuilder<SupportTicket> builder)
    {
        builder.ToTable("support_tickets");

        builder.HasKey(ticket => ticket.Id);

        builder.Property(ticket => ticket.Id)
            .HasConversion(
                id => id.Value,
                value => new SupportTicketId(value))
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(ticket => ticket.UserId)
            .HasConversion(
                id => id.Value,
                value => new UserId(value))
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(ticket => ticket.Subject)
            .HasColumnName("subject")
            .HasMaxLength(SupportTicket.MaximumSubjectLength)
            .IsRequired();

        builder.Property(ticket => ticket.Message)
            .HasColumnName("message")
            .HasMaxLength(SupportTicket.MaximumMessageLength)
            .IsRequired();

        builder.Property(ticket => ticket.Status)
            .HasColumnName("status")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(ticket => ticket.CreatedOnUtc)
            .HasColumnName("created_on_utc")
            .IsRequired();

        builder.Property(ticket => ticket.NotifiedOnUtc)
            .HasColumnName("notified_on_utc");

        builder.Ignore(ticket => ticket.IsNotified);
        builder.Ignore(ticket => ticket.DomainEvents);
    }
}
