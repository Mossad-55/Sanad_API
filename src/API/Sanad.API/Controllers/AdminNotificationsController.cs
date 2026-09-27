using System.IdentityModel.Tokens.Jwt;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Domain.Medications;
using Sanad.Modules.Identity.Application.Authentication.Tokens;
using Sanad.Modules.Notifications.Application.Notifications;

namespace Sanad.API.Controllers;

[Authorize(Policy = AuthorizationPolicies.AdminNotificationOperationalRead)]
[Route("api/v1/admin/notifications")]
public sealed class AdminNotificationsController(ISender sender, IFamiliesDbContext families) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var actor = new UserId(Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value));
        var accountType = User.FindFirst(AuthClaimNames.AccountType)!.Value;
        var correlationId = Request.Headers.TryGetValue("X-Correlation-ID", out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.ToString() : HttpContext.TraceIdentifier;

        // Persist the payload-free sensitive-read audit before querying notification records.
        families.AdminMedicationAccessAudits.Add(AdminMedicationAccessAudit.Create(
            actor, accountType, "ListNotifications", "Notification", null, DateTime.UtcNow, correlationId));
        await families.SaveChangesAsync(ct);

        return ToActionResult(await sender.Send(new ListAdminNotificationsQuery(page, pageSize), ct));
    }
}
