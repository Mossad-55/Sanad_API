using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ahlena.Modules.Notifications.Domain.Notifications;
namespace Ahlena.Modules.Notifications.Infrastructure.Persistence.Configurations;
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
        b.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(Notification.MaximumIdempotencyKeyLength);
        b.HasIndex(x => new { x.RecipientUserId, x.CreatedOnUtc, x.Id }); b.HasIndex(x => new { x.RecipientUserId, x.ReadOnUtc, x.CreatedOnUtc });
        b.HasIndex(x => x.IdempotencyKey).IsUnique();
    }
}

public sealed class EmailOutboxMessageConfiguration : IEntityTypeConfiguration<EmailOutboxMessage>
{
    public void Configure(EntityTypeBuilder<EmailOutboxMessage> b)
    {
        b.ToTable("email_outbox");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.RecipientEmail).HasColumnName("recipient_email").HasMaxLength(254).IsRequired();
        b.Property(x => x.Subject).HasColumnName("subject").HasMaxLength(500).IsRequired();
        b.Property(x => x.Body).HasColumnName("body").HasMaxLength(10000).IsRequired();
        b.Property(x => x.HtmlBody).HasColumnName("html_body").HasColumnType("text");
        b.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(300).IsRequired();
        b.Property(x => x.Status).HasColumnName("status").HasConversion<int>().IsRequired();
        b.Property(x => x.AttemptCount).HasColumnName("attempt_count").IsRequired();
        b.Property(x => x.NextAttemptOnUtc).HasColumnName("next_attempt_on_utc").IsRequired();
        b.Property(x => x.CreatedOnUtc).HasColumnName("created_on_utc").IsRequired();
        b.Property(x => x.SentOnUtc).HasColumnName("sent_on_utc");
        b.Property(x => x.LastAttemptOnUtc).HasColumnName("last_attempt_on_utc");
        b.Property(x => x.LastError).HasColumnName("last_error").HasMaxLength(2000);
        b.Property(x => x.ClaimToken).HasColumnName("claim_token");
        b.HasIndex(x => x.IdempotencyKey).IsUnique();
        b.HasIndex(x => new { x.Status, x.NextAttemptOnUtc });
    }
}

public sealed class DeviceTokenConfiguration : IEntityTypeConfiguration<DeviceToken>
{
    public void Configure(EntityTypeBuilder<DeviceToken> b)
    {
        b.ToTable("device_tokens");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
        b.Property(x => x.Platform).HasColumnName("platform").HasConversion<int>().IsRequired();
        b.Property(x => x.Token).HasColumnName("token").HasMaxLength(DeviceToken.MaximumTokenLength).IsRequired();
        b.Property(x => x.AppVersion).HasColumnName("app_version").HasMaxLength(DeviceToken.MaximumAppVersionLength);
        b.Property(x => x.RegisteredOnUtc).HasColumnName("registered_on_utc").IsRequired();
        b.Property(x => x.LastSeenOnUtc).HasColumnName("last_seen_on_utc").IsRequired();
        b.HasIndex(x => x.Token).IsUnique();
    }
}

public sealed class PushOutboxMessageConfiguration : IEntityTypeConfiguration<PushOutboxMessage>
{
    public void Configure(EntityTypeBuilder<PushOutboxMessage> b)
    {
        b.ToTable("push_outbox");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.RecipientUserId).HasColumnName("recipient_user_id").IsRequired();
        b.Property(x => x.Title).HasColumnName("title").HasMaxLength(Notification.MaximumTitleLength).IsRequired();
        b.Property(x => x.Body).HasColumnName("body").HasMaxLength(Notification.MaximumBodyLength).IsRequired();
        b.Property(x => x.DataJson).HasColumnName("data_json").HasMaxLength(PushOutboxMessage.MaximumDataLength).IsRequired();
        b.Property(x => x.PreferenceKey).HasColumnName("preference_key").HasMaxLength(80).IsRequired();
        b.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(300).IsRequired();
        b.Property(x => x.Status).HasColumnName("status").HasConversion<int>().IsRequired();
        b.Property(x => x.AttemptCount).HasColumnName("attempt_count").IsRequired();
        b.Property(x => x.NextAttemptOnUtc).HasColumnName("next_attempt_on_utc").IsRequired();
        b.Property(x => x.CreatedOnUtc).HasColumnName("created_on_utc").IsRequired();
        b.Property(x => x.SentOnUtc).HasColumnName("sent_on_utc");
        b.Property(x => x.LastAttemptOnUtc).HasColumnName("last_attempt_on_utc");
        b.Property(x => x.LastErrorCode).HasColumnName("last_error_code").HasMaxLength(80);
        b.Property(x => x.FcmMessageId).HasColumnName("fcm_message_id").HasMaxLength(256);
        b.Property(x => x.ClaimToken).HasColumnName("claim_token");
        b.HasIndex(x => x.IdempotencyKey).IsUnique();
        b.HasIndex(x => new { x.Status, x.NextAttemptOnUtc });
    }
}
