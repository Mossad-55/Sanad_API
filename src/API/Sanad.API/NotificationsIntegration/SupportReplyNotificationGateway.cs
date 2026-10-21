using MediatR;
using Sanad.Modules.Identity.Application.Abstractions.Messaging;
using Sanad.Modules.Notifications.Application.Abstractions.Recipients;
using Sanad.Modules.Notifications.Application.Notifications;

namespace Sanad.API.NotificationsIntegration;

public sealed class SupportReplyNotificationGateway :
    ISupportReplyNotifier
{
    private readonly ISender _sender;
    private readonly INotificationLanguageGateway _languages;

    public SupportReplyNotificationGateway(
        ISender sender,
        INotificationLanguageGateway languages)
    {
        _sender = sender;
        _languages = languages;
    }

    public async Task TryNotifyAsync(
        Guid recipientUserId,
        Guid ticketId,
        string body,
        CancellationToken cancellationToken)
    {
        // F8: only the title is chosen from the catalog, in the recipient's
        // persisted language. The body is what the administrator typed and is
        // stored byte for byte.
        var recipientLanguages = await _languages.GetUiLanguagesAsync(
            [recipientUserId], cancellationToken);
        string title = NotificationTextCatalog.SupportReplyTitle(
            NotificationTextCatalog.ResolveLanguage(recipientLanguages, recipientUserId));

        await _sender.Send(
            new CreateInAppNotificationCommand(
                recipientUserId,
                "Support",
                "SupportReply",
                title,
                body,
                "SupportTicket",
                ticketId,
                DateTime.UtcNow),
            cancellationToken);
    }
}