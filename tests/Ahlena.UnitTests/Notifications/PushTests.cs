using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ahlena.API.Controllers;
using Ahlena.Modules.Notifications.Application.Abstractions.Push;
using Ahlena.Modules.Notifications.Application.Notifications;
using Ahlena.Modules.Notifications.Domain.Notifications;
using Ahlena.Modules.Notifications.Infrastructure.Persistence;

namespace Ahlena.UnitTests.Notifications;

public sealed class PushTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void DeviceToken_RegistersAndMasksTheSecret()
    {
        const string token = "synthetic_fcm_token_0123456789";
        DeviceToken device = DeviceToken.Register(Guid.NewGuid(), DevicePlatform.Android, token, "1.2.3", Now);
        Assert.Equal("…23456789"[^6..], DeviceToken.Mask(token)[^6..]);
        Assert.DoesNotContain(token, DeviceToken.Mask(token), StringComparison.Ordinal);
        Assert.Equal(Now, device.RegisteredOnUtc);
        Assert.Equal(Now, device.LastSeenOnUtc);
    }

    [Fact]
    public void DeviceToken_RejectsWhitespaceAndNonUtcTime()
    {
        Assert.Throws<ArgumentException>(() => DeviceToken.Register(Guid.NewGuid(), DevicePlatform.Android,
            "synthetic token with spaces", null, Now));
        Assert.Throws<ArgumentException>(() => DeviceToken.Register(Guid.NewGuid(), DevicePlatform.Android,
            "synthetic_fcm_token_0123456789", null, DateTime.SpecifyKind(Now, DateTimeKind.Local)));
    }

    [Fact]
    public void PushOutbox_UsesCappedRetryAndStopsAfterMaximumAttempts()
    {
        PushOutboxMessage message = NewPushMessage();
        for (int i = 1; i <= 8; i++)
        {
            Guid claim = Guid.NewGuid();
            message.MarkProcessing(Now.AddMinutes(i), claim);
            message.MarkFailed("Push.ProviderFailure", Now.AddMinutes(i), claim, retryable: true);
        }
        Assert.Equal(PushOutboxStatus.Failed, message.Status);
        Assert.Equal(8, message.AttemptCount);
        Assert.Equal("Push.ProviderFailure", message.LastErrorCode);
    }

    [Fact]
    public void PushOutbox_RejectsCompletionFromAnotherClaim()
    {
        PushOutboxMessage message = NewPushMessage();
        Guid claim = Guid.NewGuid();
        message.MarkProcessing(Now, claim);
        Assert.Throws<InvalidOperationException>(() => message.MarkSent(Now, Guid.NewGuid(), "test-id"));
    }

    [Fact]
    public async Task FcmSender_InvalidConfigurationFailsClosedWithoutSending()
    {
        using var sender = new Ahlena.Modules.Notifications.Infrastructure.Push.FcmPushSender(
            Options.Create(new Ahlena.Modules.Notifications.Infrastructure.Push.PushFirebaseOptions
            {
                ServiceAccountJson = "{invalid-json"
            }));
        Assert.False(sender.IsConfigured);
        PushSendResult result = await sender.SendAsync("synthetic_fcm_token_0123456789", "title", "body",
            new Dictionary<string, string>(), CancellationToken.None);
        Assert.Equal(PushSendOutcome.NotConfigured, result.Outcome);
        Assert.Equal("Push.NotConfigured", result.ErrorCode);
    }
    [Fact]
    public async Task DevicesController_RejectsUnconfiguredPushWithoutPersistingToken()
    {
        await using var context = CreateContext();
        var controller = CreateController(context, new FakePushSender(configured: false), Guid.NewGuid());
        IActionResult result = await controller.Register(new DevicesController.RegisterDeviceRequest(
            "android", "synthetic_fcm_token_0123456789", null), CancellationToken.None);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, Assert.IsType<ObjectResult>(result).StatusCode);
        Assert.Empty(context.DeviceTokens);
    }

    [Fact]
    public async Task DevicesController_UpsertsAndMasksTokensForOwner()
    {
        await using var context = CreateContext();
        Guid owner = Guid.NewGuid();
        var controller = CreateController(context, new FakePushSender(configured: true), owner);
        var request = new DevicesController.RegisterDeviceRequest("iOS", "synthetic_fcm_token_0123456789", "2.0");
        var first = Assert.IsType<OkObjectResult>(await controller.Register(request, CancellationToken.None));
        Guid firstId = Assert.IsType<DevicesController.DeviceRegisteredResponse>(first.Value).DeviceId;
        var second = Assert.IsType<OkObjectResult>(await controller.Register(request with { AppVersion = "2.1" }, CancellationToken.None));
        Assert.Equal(firstId, Assert.IsType<DevicesController.DeviceRegisteredResponse>(second.Value).DeviceId);
        Assert.Single(context.DeviceTokens);

        var list = Assert.IsType<OkObjectResult>(await controller.List(CancellationToken.None));
        var item = Assert.IsType<DevicesController.DeviceTokenResponse>(Assert.Single(Assert.IsAssignableFrom<IEnumerable<DevicesController.DeviceTokenResponse>>(list.Value)));
        Assert.Equal("IOS", item.Platform);
        Assert.EndsWith("456789", item.MaskedToken, StringComparison.Ordinal);
        Assert.DoesNotContain("synthetic_fcm_token", item.MaskedToken, StringComparison.Ordinal);
        Assert.Equal("2.1", item.AppVersion);
    }

    [Fact]
    public async Task DevicesController_DeleteIsScopedToAuthenticatedOwner()
    {
        await using var context = CreateContext();
        Guid owner = Guid.NewGuid();
        DeviceToken device = DeviceToken.Register(owner, DevicePlatform.Android, "synthetic_fcm_token_0123456789", null, Now);
        context.DeviceTokens.Add(device);
        await context.SaveChangesAsync();
        var controller = CreateController(context, new FakePushSender(configured: true), Guid.NewGuid());
        Assert.IsType<NoContentResult>(await controller.Unregister(device.Id, CancellationToken.None));
        Assert.Single(context.DeviceTokens);
    }

    [Fact]
    public async Task DevicesController_RejectsMalformedToken()
    {
        await using var context = CreateContext();
        var controller = CreateController(context, new FakePushSender(configured: true), Guid.NewGuid());
        IActionResult result = await controller.Register(new DevicesController.RegisterDeviceRequest("android", "bad token", null), CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(context.DeviceTokens);
    }

    private static PushOutboxMessage NewPushMessage() => PushOutboxMessage.Create(Guid.NewGuid(), "title", "body",
        new Dictionary<string, string> { ["entityId"] = Guid.NewGuid().ToString("D") }, "test:push:1", "BookingUpdates", Now);

    private static NotificationsDbContext CreateContext() => new(new DbContextOptionsBuilder<NotificationsDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private static DevicesController CreateController(NotificationsDbContext context, IPushSender sender, Guid userId)
    {
        var controller = new DevicesController(context, sender)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([
                        new Claim(JwtRegisteredClaimNames.Sub, userId.ToString("D"))], "test"))
                }
            }
        };
        return controller;
    }

    private sealed class FakePushSender(bool configured) : IPushSender
    {
        public bool IsConfigured => configured;
        public Task<PushSendResult> SendAsync(string token, string title, string body,
            IReadOnlyDictionary<string, string> data, CancellationToken cancellationToken) =>
            Task.FromResult(new PushSendResult(PushSendOutcome.Sent, "unit-test-message"));
    }
}
