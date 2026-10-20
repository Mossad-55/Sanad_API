using MediatR;
using Sanad.Modules.Identity.Application.Abstractions.Messaging;
using Sanad.Modules.Notifications.Application.Notifications;

namespace Sanad.API.NotificationsIntegration;

public sealed class SupportReplyNotificationGateway :
    ISupportReplyNotifier
{
    private readonly ISender _sender;

    public SupportReplyNotificationGateway(
        ISender sender)
    {
        _sender = sender;
    }

    public async Task TryNotifyAsync(
        Guid recipientUserId,
        Guid ticketId,
        string body,
        CancellationToken cancellationToken)
    {
        await _sender.Send(
            new CreateInAppNotificationCommand(
                recipientUserId,
                "Support",
                "SupportReply",
                "Support reply",
                body,
                "SupportTicket",
                ticketId,
                DateTime.UtcNow),
            cancellationToken);
    }
}