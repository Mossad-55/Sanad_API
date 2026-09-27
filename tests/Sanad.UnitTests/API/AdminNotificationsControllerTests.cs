using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Sanad.API.Authorization;
using Sanad.API.Controllers;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Domain.Medications;
using Sanad.Modules.Families.Infrastructure.Persistence;
using Sanad.Modules.Identity.Application.Authentication.Tokens;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.Modules.Notifications.Application.Notifications;

namespace Sanad.UnitTests.API;

public sealed class AdminNotificationsControllerTests
{
    [Fact]
    public void Controller_UsesOperationalReadPolicyAndExposesAllFourRoutes()
    {
        var controller = typeof(AdminNotificationsController);
        var authorization = Assert.Single(controller.GetCustomAttributes<AuthorizeAttribute>());
        Assert.Equal(AuthorizationPolicies.AdminNotificationOperationalRead, authorization.Policy);
        Assert.Equal("api/v1/admin/notifications", Assert.Single(controller.GetCustomAttributes<RouteAttribute>()).Template);
        AssertRoute(nameof(AdminNotificationsController.List), "");
        AssertRoute(nameof(AdminNotificationsController.Get), "{notificationId:guid}");
        AssertRoute(nameof(AdminNotificationsController.Timeline), "timeline");
        AssertRoute(nameof(AdminNotificationsController.Aggregate), "aggregate");
    }

    [Fact]
    public void OperationalPolicy_AllowsOnlyNormalSuperAndSupportAdmins()
    {
        var policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireClaim(AuthClaimNames.AccessType, AuthAccessType.Normal.ToString())
            .RequireClaim(AuthClaimNames.AccountType, AccountType.SuperAdmin.ToString(), AccountType.SupportAdmin.ToString())
            .Build();

        Assert.True(Satisfies(policy, AccountType.SuperAdmin, AuthAccessType.Normal));
        Assert.True(Satisfies(policy, AccountType.SupportAdmin, AuthAccessType.Normal));
        foreach (var account in new[] { AccountType.ContentAdmin, AccountType.Family, AccountType.Elderly })
            Assert.False(Satisfies(policy, account, AuthAccessType.Normal));
        Assert.False(Satisfies(policy, AccountType.SupportAdmin, AuthAccessType.RestrictedVerification));
    }

    [Theory]
    [InlineData("list", "ListNotifications", "NotificationList")]
    [InlineData("detail", "GetNotification", "Notification")]
    [InlineData("timeline", "GetNotificationTimeline", "NotificationTimeline")]
    [InlineData("aggregate", "GetNotificationAggregate", "NotificationAggregate")]
    public async Task Routes_PersistActorAndAuditMarkerBeforeDispatch(string route, string action, string resourceType)
    {
        await using var families = CreateFamiliesDb();
        var actorId = Guid.NewGuid();
        var sender = new CapturingSender(() => families.AdminMedicationAccessAudits.AsNoTracking().CountAsync());
        var controller = CreateController(sender, actorId, families);
        controller.HttpContext.Request.Headers["X-Correlation-ID"] = "admin-notification-test";
        var notificationId = Guid.NewGuid();

        var result = route switch
        {
            "list" => await controller.List(2, 17, "Sos", "Created", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 3), CancellationToken.None),
            "detail" => await controller.Get(notificationId, CancellationToken.None),
            "timeline" => await controller.Timeline(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 2), 3, 11, CancellationToken.None),
            "aggregate" => await controller.Aggregate(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 2), CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(route))
        };

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(1, sender.AuditCountWhenCalled);
        var audit = await families.AdminMedicationAccessAudits.AsNoTracking().SingleAsync();
        Assert.Equal(new UserId(actorId), audit.ActorUserId);
        Assert.Equal(AccountType.SupportAdmin.ToString(), audit.ActorAccountType);
        Assert.Equal(action, audit.Action);
        Assert.Equal(resourceType, audit.ResourceType);
        Assert.Equal(route == "detail" ? notificationId : null, audit.ResourceId);
        Assert.Equal("admin-notification-test", audit.CorrelationId);

        switch (sender.LastRequest)
        {
            case ListAdminNotificationsQuery query:
                Assert.Equal(2, query.Page);
                Assert.Equal(17, query.PageSize);
                Assert.Equal("Sos", query.Category);
                Assert.Equal("Created", query.Type);
                Assert.Equal(new DateOnly(2026, 8, 1), query.StartDate);
                Assert.Equal(new DateOnly(2026, 8, 3), query.EndDate);
                break;
            case GetAdminNotificationQuery query:
                Assert.Equal(notificationId, query.NotificationId);
                break;
            case GetAdminNotificationTimelineQuery query:
                Assert.Equal(new DateOnly(2026, 8, 1), query.StartDate);
                Assert.Equal(new DateOnly(2026, 8, 2), query.EndDate);
                Assert.Equal(3, query.Page);
                Assert.Equal(11, query.PageSize);
                break;
            case GetAdminNotificationAggregateQuery query:
                Assert.Equal(new DateOnly(2026, 8, 1), query.StartDate);
                Assert.Equal(new DateOnly(2026, 8, 2), query.EndDate);
                break;
            default:
                throw new Xunit.Sdk.XunitException($"Unexpected query: {sender.LastRequest?.GetType().Name ?? "null"}");
        }
    }

    [Fact]
    public async Task Route_DoesNotDispatchWhenAuditPersistenceFails()
    {
        var options = new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new FailingSaveChangesInterceptor())
            .Options;
        await using var families = new FamiliesDbContext(options);
        var sender = new CapturingSender();
        var controller = CreateController(sender, Guid.NewGuid(), families);

        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.Timeline(
            new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 2), 1, 20, CancellationToken.None));

        Assert.Equal(0, sender.CallCount);
    }

    [Fact]
    public void ResponseProjection_ContainsOnlyDocumentedMetadata()
    {
        var properties = typeof(AdminNotificationRecord).GetProperties().Select(x => x.Name).Order().ToArray();
        Assert.Equal(new[] { "Category", "CreatedOnUtc", "Id", "ReadOnUtc", "Type" }.Order(), properties);
    }

    private static void AssertRoute(string methodName, string expectedTemplate)
    {
        var method = typeof(AdminNotificationsController).GetMethod(methodName)!;
        Assert.Equal(expectedTemplate, method.GetCustomAttribute<HttpGetAttribute>()!.Template ?? "");
    }

    private static AdminNotificationsController CreateController(CapturingSender sender, Guid actorId, IFamiliesDbContext families)
    {
        var controller = new AdminNotificationsController(sender, families)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(JwtRegisteredClaimNames.Sub, actorId.ToString()),
            new Claim(AuthClaimNames.AccountType, AccountType.SupportAdmin.ToString())]));
        return controller;
    }

    private static FamiliesDbContext CreateFamiliesDb() => new(new DbContextOptionsBuilder<FamiliesDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static bool Satisfies(AuthorizationPolicy policy, AccountType account, AuthAccessType access)
        => policy.Requirements.All(requirement => requirement switch
        {
            DenyAnonymousAuthorizationRequirement => true,
            ClaimsAuthorizationRequirement claim => claim.AllowedValues?.Contains(
                claim.ClaimType == AuthClaimNames.AccountType ? account.ToString() : access.ToString()) == true,
            _ => false
        });

    private sealed class CapturingSender(Func<Task<int>>? auditCount = null) : MediatR.ISender
    {
        public object? LastRequest { get; private set; }
        public int CallCount { get; private set; }
        public int AuditCountWhenCalled { get; private set; }

        public async Task<TResponse> Send<TResponse>(MediatR.IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastRequest = request;
            if (auditCount is not null) AuditCountWhenCalled = await auditCount();
            object response = request switch
            {
                ListAdminNotificationsQuery => Result<AdminNotificationPage>.Success(new AdminNotificationPage([], 1, 20, false)),
                GetAdminNotificationQuery => Result<AdminNotificationRecord>.Success(default!),
                GetAdminNotificationTimelineQuery query => Result<AdminNotificationPage>.Success(new AdminNotificationPage([], query.Page, query.PageSize, false)),
                GetAdminNotificationAggregateQuery => Result<AdminNotificationAggregate>.Success(new AdminNotificationAggregate(0, 0, 0, [])),
                _ => throw new NotSupportedException(request.GetType().Name)
            };
            return (TResponse)response;
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : MediatR.IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(MediatR.IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FailingSaveChangesInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
            => ValueTask.FromException<InterceptionResult<int>>(new InvalidOperationException("audit unavailable"));
    }
}
