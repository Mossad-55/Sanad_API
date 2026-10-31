using System.IdentityModel.Tokens.Jwt;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc;
using Ahlena.API.Authorization;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Families.Application.Abstractions.Data;
using Ahlena.Modules.Families.Domain.Medications;
using Ahlena.Modules.Identity.Application.Authentication.Tokens;
using Ahlena.Modules.Notifications.Application.Notifications;

namespace Ahlena.API.Controllers;

[Authorize(Policy = AuthorizationPolicies.AdminNotificationOperationalRead)]
[Route("api/v1/admin/notifications")]
public sealed class AdminNotificationsController(ISender sender, IFamiliesDbContext families) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(AdminNotificationPage), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] string? category = null, [FromQuery] string? type = null,
        [FromQuery] DateOnly? startDate = null, [FromQuery] DateOnly? endDate = null, CancellationToken ct = default)
    {
        await AuditAsync("ListNotifications", "NotificationList", null, ct);
        return ToActionResult(await sender.Send(new ListAdminNotificationsQuery(page, pageSize, category, type, startDate, endDate), ct));
    }

    [HttpGet("{notificationId:guid}")]
    [ProducesResponseType(typeof(AdminNotificationRecord), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid notificationId, CancellationToken ct)
    {
        await AuditAsync("GetNotification", "Notification", notificationId, ct);
        return ToActionResult(await sender.Send(new GetAdminNotificationQuery(notificationId), ct));
    }

    [HttpGet("timeline")]
    [ProducesResponseType(typeof(AdminNotificationPage), StatusCodes.Status200OK)]
    public async Task<IActionResult> Timeline(
        [FromQuery, BindRequired] DateOnly startDate,
        [FromQuery, BindRequired] DateOnly endDate,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        await AuditAsync("GetNotificationTimeline", "NotificationTimeline", null, ct);
        return ToActionResult(await sender.Send(new GetAdminNotificationTimelineQuery(startDate, endDate, page, pageSize), ct));
    }

    [HttpGet("aggregate")]
    [ProducesResponseType(typeof(AdminNotificationAggregate), StatusCodes.Status200OK)]
    public async Task<IActionResult> Aggregate(
        [FromQuery] DateOnly? startDate = null,
        [FromQuery] DateOnly? endDate = null,
        CancellationToken ct = default)
    {
        await AuditAsync("GetNotificationAggregate", "NotificationAggregate", null, ct);
        return ToActionResult(await sender.Send(new GetAdminNotificationAggregateQuery(startDate, endDate), ct));
    }

    private async Task AuditAsync(string action, string resourceType, Guid? resourceId, CancellationToken ct)
    {
        var actor = new UserId(Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value));
        var accountType = User.FindFirst(AuthClaimNames.AccountType)!.Value;
        var correlationId = Request.Headers.TryGetValue("X-Correlation-ID", out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.ToString() : HttpContext.TraceIdentifier;

        // Persist the payload-free sensitive-read audit before dispatching any notification query.
        families.AdminMedicationAccessAudits.Add(AdminMedicationAccessAudit.Create(
            actor, accountType, action, resourceType, resourceId, DateTime.UtcNow, correlationId));
        await families.SaveChangesAsync(ct);
    }
}
