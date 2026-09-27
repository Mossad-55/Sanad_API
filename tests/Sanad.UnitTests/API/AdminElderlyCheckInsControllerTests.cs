using System.Reflection;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.API.Controllers;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.CheckIns;
using Sanad.Modules.Identity.Application.Authentication.Tokens;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.UnitTests.API;

public sealed class AdminElderlyCheckInsControllerTests
{
    [Fact]
    public void RequiresOperationalReadPolicy_AndExposesListDetailTimelineAndAggregateRoutes()
    {
        var authorization = Assert.Single(typeof(AdminElderlyCheckInsController).GetCustomAttributes<AuthorizeAttribute>());
        Assert.Equal(AuthorizationPolicies.ElderlyMedicationOperationalRead, authorization.Policy);
        Assert.Equal("api/v1/admin/elderly/check-ins", Assert.Single(typeof(AdminElderlyCheckInsController).GetCustomAttributes<RouteAttribute>()).Template);
        Assert.Null(typeof(AdminElderlyCheckInsController).GetMethod(nameof(AdminElderlyCheckInsController.List))!.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal("{checkInId:guid}", typeof(AdminElderlyCheckInsController).GetMethod(nameof(AdminElderlyCheckInsController.Get))!.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal("timeline/{elderlyId:guid}", typeof(AdminElderlyCheckInsController).GetMethod(nameof(AdminElderlyCheckInsController.Timeline))!.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal("aggregate", typeof(AdminElderlyCheckInsController).GetMethod(nameof(AdminElderlyCheckInsController.Aggregate))!.GetCustomAttribute<HttpGetAttribute>()!.Template);
    }

    [Fact]
    public void OperationalReadPolicy_AllowsOnlyNormalSuperOrSupportAdmin()
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
        Assert.False(Satisfies(policy, AccountType.SuperAdmin, AuthAccessType.RestrictedVerification));
    }

    [Fact]
    public async Task Routes_UseAuthenticatedActorAccountTypeAndCorrelationId()
    {
        var actor = UserId.New();
        var sender = new CapturingSender();
        var controller = new AdminElderlyCheckInsController(sender)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(JwtRegisteredClaimNames.Sub, actor.Value.ToString()),
            new Claim(AuthClaimNames.AccountType, AccountType.SupportAdmin.ToString())]));
        controller.HttpContext.Request.Headers["X-Correlation-ID"] = "corr-1";

        var elderlyId = Guid.NewGuid();
        var result = await controller.Timeline(elderlyId, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 2), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        var query = Assert.IsType<GetAdminElderlyCheckInTimelineQuery>(sender.LastRequest);
        Assert.Equal(actor, query.ActorUserId);
        Assert.Equal(AccountType.SupportAdmin.ToString(), query.ActorAccountType);
        Assert.Equal("corr-1", query.CorrelationId);
        Assert.Equal(elderlyId, query.ElderlyId);
    }

    [Fact]
    public async Task MissingCheckInMapsToNotFoundProblemDetails()
    {
        var sender = new CapturingSender(Result<AdminElderlyCheckInRecord>.Failure(
            new Error("Families.AdminCheckIn.NotFound", "missing")));
        var controller = Create(sender);

        var result = await controller.Get(Guid.NewGuid(), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, objectResult.StatusCode);
        Assert.Equal("Families.AdminCheckIn.NotFound", ((ProblemDetails)objectResult.Value!).Extensions["code"]);
    }

    private static AdminElderlyCheckInsController Create(CapturingSender sender)
    {
        var controller = new AdminElderlyCheckInsController(sender)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
            new Claim(AuthClaimNames.AccountType, AccountType.SupportAdmin.ToString())]));
        return controller;
    }

    private static bool Satisfies(AuthorizationPolicy policy, AccountType account, AuthAccessType access)
    {
        var claims = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(AuthClaimNames.AccountType, account.ToString()),
            new Claim(AuthClaimNames.AccessType, access.ToString())]));
        return policy.Requirements.All(requirement => requirement switch
        {
            DenyAnonymousAuthorizationRequirement => true,
            ClaimsAuthorizationRequirement claim => claim.AllowedValues?.Contains(claim.ClaimType == AuthClaimNames.AccountType ? account.ToString() : access.ToString()) == true,
            _ => false
        });
    }

    private sealed class CapturingSender(object? response = null) : MediatR.ISender
    {
        public object? LastRequest { get; private set; }

        public Task<TResponse> Send<TResponse>(MediatR.IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            if (response is not null) return Task.FromResult((TResponse)response);
            var result = request switch
            {
                GetAdminElderlyCheckInTimelineQuery => Result<IReadOnlyList<AdminElderlyCheckInRecord>>.Success([]),
                GetAdminElderlyCheckInQuery => Result<AdminElderlyCheckInRecord>.Success(default!),
                _ => Result.Success()
            };
            return Task.FromResult((TResponse)(object)result);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : MediatR.IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(MediatR.IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
