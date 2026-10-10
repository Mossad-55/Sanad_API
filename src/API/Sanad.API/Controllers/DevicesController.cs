using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sanad.API.Authorization;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Notifications.Application.Abstractions.Push;
using Sanad.Modules.Notifications.Domain.Notifications;
using Sanad.Modules.Notifications.Infrastructure.Persistence;

namespace Sanad.API.Controllers;

[Authorize(Policy = AuthorizationPolicies.NormalAccess)]
[Route("api/v1/devices")]
public sealed class DevicesController(NotificationsDbContext db, IPushSender pushSender) : ApiControllerBase
{
    private static readonly Regex TokenPattern = new(@"^[A-Za-z0-9_:.=+\-/]{20,2048}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [HttpPost]
    [ProducesResponseType(typeof(DeviceRegisteredResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Register([FromBody] RegisterDeviceRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId)) return Unauthorized();
        if (!pushSender.IsConfigured)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { code = "Push.NotConfigured" });
        if (!TryParsePlatform(request.Platform, out DevicePlatform platform))
            return BadRequest(new { code = "Devices.InvalidPlatform" });
        if (request.Token is null || !TokenPattern.IsMatch(request.Token) || request.Token.Any(char.IsControl))
            return BadRequest(new { code = "Devices.InvalidToken" });
        if (request.AppVersion?.Length > DeviceToken.MaximumAppVersionLength)
            return BadRequest(new { code = "Devices.InvalidAppVersion" });

        DateTime now = DateTime.UtcNow;
        DeviceToken? device = await db.DeviceTokens.SingleOrDefaultAsync(x => x.Token == request.Token, cancellationToken);
        if (device is null)
        {
            device = DeviceToken.Register(userId.Value, platform, request.Token, request.AppVersion, now);
            db.DeviceTokens.Add(device);
        }
        else
        {
            device.Reassign(userId.Value, platform, request.AppVersion, now);
        }
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new DeviceRegisteredResponse(device.Id, true));
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId)) return Unauthorized();
        var devices = await db.DeviceTokens.AsNoTracking().Where(x => x.UserId == userId.Value)
            .OrderByDescending(x => x.LastSeenOnUtc)
            .Select(x => new DeviceTokenResponse(x.Id, x.Platform.ToString(), DeviceToken.Mask(x.Token), x.AppVersion,
                x.RegisteredOnUtc, x.LastSeenOnUtc))
            .ToListAsync(cancellationToken);
        return Ok(devices);
    }

    [HttpDelete("{deviceId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Unregister(Guid deviceId, CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId)) return Unauthorized();
        DeviceToken? device = await db.DeviceTokens.SingleOrDefaultAsync(x => x.Id == deviceId && x.UserId == userId.Value, cancellationToken);
        if (device is not null)
        {
            db.DeviceTokens.Remove(device);
            await db.SaveChangesAsync(cancellationToken);
        }
        return NoContent();
    }

    private static bool TryParsePlatform(string value, out DevicePlatform platform)
    {
        if (string.Equals(value, "android", StringComparison.OrdinalIgnoreCase))
        { platform = DevicePlatform.Android; return true; }
        if (string.Equals(value, "ios", StringComparison.OrdinalIgnoreCase))
        { platform = DevicePlatform.IOS; return true; }
        platform = default;
        return false;
    }

    public sealed record RegisterDeviceRequest(string Platform, string Token, string? AppVersion);
    public sealed record DeviceRegisteredResponse(Guid DeviceId, bool PushEnabled);
    public sealed record DeviceTokenResponse(Guid DeviceId, string Platform, string MaskedToken, string? AppVersion,
        DateTime RegisteredOnUtc, DateTime LastSeenOnUtc);
}
