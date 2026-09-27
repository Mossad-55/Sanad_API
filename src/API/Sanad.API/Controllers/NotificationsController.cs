using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Notifications.Application.Notifications;

namespace Sanad.API.Controllers;

[Authorize(Policy = AuthorizationPolicies.NormalAccess)]
[Route("api/v1/notifications")]
public sealed class NotificationsController(ISender sender) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(NotificationPage), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] string? cursor, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId)) return Unauthorized();
        return ToActionResult(await sender.Send(new ListNotificationsQuery(userId.Value, cursor, pageSize), cancellationToken));
    }

    [HttpGet("unread-count")]
    public async Task<IActionResult> UnreadCount(CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId)) return Unauthorized();
        return ToActionResult(await sender.Send(new GetUnreadNotificationCountQuery(userId.Value), cancellationToken));
    }

    [HttpPut("{notificationId:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid notificationId, CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId)) return Unauthorized();
        return ToActionResult(await sender.Send(new MarkNotificationReadCommand(userId.Value, notificationId), cancellationToken));
    }

    [HttpPut("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId)) return Unauthorized();
        return ToActionResult(await sender.Send(new MarkAllNotificationsReadCommand(userId.Value), cancellationToken));
    }
}
