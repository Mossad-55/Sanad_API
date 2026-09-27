using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.Modules.Notifications.Domain.Notifications;
namespace Sanad.Modules.Notifications.Infrastructure.Persistence.Configurations;
public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.ToTable("notifications"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.RecipientUserId).HasColumnName("recipient_user_id").IsRequired();
        b.Property(x => x.Category).HasColumnName("category").HasMaxLength(Notification.MaximumCategoryLength).IsRequired();
        b.Property(x => x.Type).HasColumnName("type").HasMaxLength(Notification.MaximumTypeLength).IsRequired();
        b.Property(x => x.Title).HasColumnName("title").HasMaxLength(Notification.MaximumTitleLength).IsRequired();
        b.Property(x => x.Body).HasColumnName("body").HasMaxLength(Notification.MaximumBodyLength).IsRequired();
        b.Property(x => x.DestinationEntityKind).HasColumnName("destination_entity_kind").HasMaxLength(Notification.MaximumDestinationKindLength).IsRequired();
        b.Property(x => x.DestinationEntityId).HasColumnName("destination_entity_id").IsRequired();
        b.Property(x => x.CreatedOnUtc).HasColumnName("created_on_utc").IsRequired(); b.Property(x => x.ReadOnUtc).HasColumnName("read_on_utc");
        b.HasIndex(x => new { x.RecipientUserId, x.CreatedOnUtc, x.Id }); b.HasIndex(x => new { x.RecipientUserId, x.ReadOnUtc, x.CreatedOnUtc });
    }
}
