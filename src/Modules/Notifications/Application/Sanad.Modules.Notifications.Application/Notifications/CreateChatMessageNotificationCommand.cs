using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Notifications.Application.Abstractions.Data;
using Sanad.Modules.Notifications.Application.Abstractions.Recipients;
using Sanad.Modules.Notifications.Domain.Notifications;

namespace Sanad.Modules.Notifications.Application.Notifications;

/// <summary>
/// Lane B — new-message in-app alert for chat, produced after a send
/// commits. Frozen contract: ids only. The command carries the conversation
/// id and the message id and never any message text, and the row it writes
/// repeats those ids in the structured destination and the idempotency key
/// while its localized copy stays a generic phrase — no chat content can
/// reach the inbox through this seam, in either language (F8).
/// Idempotency: <c>chat-message:{messageId:N}:{recipientId:N}</c> is unique
/// per (message, recipient), so each recipient is alerted at most once per
/// message even under a concurrent duplicate call; a repeat for a key that
/// is already committed returns the existing notification id and writes
/// nothing.
/// </summary>
public sealed record CreateChatMessageNotificationCommand(
    Guid RecipientUserId, Guid ConversationId, Guid MessageId, DateTime CreatedOnUtc)
    : ICommand<Guid>;

public sealed class CreateChatMessageNotificationCommandValidator
    : AbstractValidator<CreateChatMessageNotificationCommand>
{
    public CreateChatMessageNotificationCommandValidator()
    {
        RuleFor(x => x.RecipientUserId).NotEmpty();
        RuleFor(x => x.ConversationId).NotEmpty();
        RuleFor(x => x.MessageId).NotEmpty();
        RuleFor(x => x.CreatedOnUtc).Must(x => x.Kind == DateTimeKind.Utc);
    }
}

public sealed class CreateChatMessageNotificationCommandHandler(
    INotificationsDbContext db,
    INotificationLanguageGateway languages)
    : ICommandHandler<CreateChatMessageNotificationCommand, Guid>
{
    public async Task<Result<Guid>> Handle(
        CreateChatMessageNotificationCommand r,
        CancellationToken ct)
    {
        string key = BuildIdempotencyKey(r.MessageId, r.RecipientUserId);

        Guid? committedId = await db.Notifications
            .Where(notification => notification.IdempotencyKey == key)
            .Select(notification => (Guid?)notification.Id)
            .SingleOrDefaultAsync(ct);

        if (committedId is not null)
        {
            // Idempotent replay: the alert row already exists, so nothing is
            // written a second time.
            return committedId.Value;
        }

        // F8: the recipient's own persisted language decides the copy, and the
        // copy is generic - the conversation and message ids stay on the
        // structured destination and on the idempotency key above.
        var recipientLanguages = await languages.GetUiLanguagesAsync([r.RecipientUserId], ct);
        NotificationCopy copy = NotificationTextCatalog.ChatNewMessage(
            NotificationTextCatalog.ResolveLanguage(recipientLanguages, r.RecipientUserId));

        var notification = Notification.Create(
            r.RecipientUserId,
            "Chat",
            "NewMessage",
            copy.Title,
            copy.Body,
            "ChatConversation",
            r.ConversationId,
            r.CreatedOnUtc,
            key);

        db.Notifications.Add(notification);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // A concurrent producer may have won the unique idempotency-key
            // race. Only treat the failure as an idempotent replay when the
            // key is now committed; unrelated persistence failures must still
            // surface to the caller.
            if (db is DbContext context)
                context.Entry(notification).State = EntityState.Detached;

            Guid? racedId = await db.Notifications
                .Where(existing => existing.IdempotencyKey == key)
                .Select(existing => (Guid?)existing.Id)
                .SingleOrDefaultAsync(ct);

            if (racedId is null)
                throw;

            return racedId.Value;
        }

        return notification.Id;
    }

    /// <summary>
    /// Per-recipient dedupe key: one alert per (message, recipient). The
    /// message id anchors the key so re-sending or re-alerting under another
    /// client key can never collide, and the recipient id keeps a future
    /// multi-recipient variant from collapsing distinct alerts.
    /// </summary>
    public static string BuildIdempotencyKey(Guid messageId, Guid recipientUserId) =>
        $"chat-message:{messageId:N}:{recipientUserId:N}";
}
