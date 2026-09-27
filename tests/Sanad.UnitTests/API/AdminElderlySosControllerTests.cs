using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.API.Controllers;
using Sanad.API.ProblemDetail;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Identity.Application.Authentication.Tokens;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.UnitTests.API;

public sealed class AdminElderlySosControllerTests
{
    [Fact]
    public void Controller_UsesOperationalPolicy_AndExposesListDetailHistoryStatus()
    {
        Assert.Equal(AuthorizationPolicies.ElderlySosOperational, Assert.Single(typeof(AdminElderlySosController).GetCustomAttributes<AuthorizeAttribute>()).Policy);
        Assert.Equal("api/v1/admin/elderly/sos", Assert.Single(typeof(AdminElderlySosController).GetCustomAttributes<RouteAttribute>()).Template);
        Assert.Equal("{sosId:guid}/history", typeof(AdminElderlySosController).GetMethod(nameof(AdminElderlySosController.History))!.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal("{sosId:guid}/status", typeof(AdminElderlySosController).GetMethod(nameof(AdminElderlySosController.Status))!.GetCustomAttribute<HttpPostAttribute>()!.Template);
    }

    [Fact]
    public void OperationalPolicy_AllowsOnlyNormalSupportOrSuperAdmin()
    {
        var policy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().RequireClaim(AuthClaimNames.AccessType, AuthAccessType.Normal.ToString()).RequireClaim(AuthClaimNames.AccountType, AccountType.SupportAdmin.ToString(), AccountType.SuperAdmin.ToString()).Build();
        Assert.True(Satisfies(policy, AccountType.SupportAdmin, AuthAccessType.Normal)); Assert.True(Satisfies(policy, AccountType.SuperAdmin, AuthAccessType.Normal));
        Assert.False(Satisfies(policy, AccountType.ContentAdmin, AuthAccessType.Normal)); Assert.False(Satisfies(policy, AccountType.Family, AuthAccessType.Normal)); Assert.False(Satisfies(policy, AccountType.SupportAdmin, AuthAccessType.RestrictedVerification));
    }

    [Theory]
    [InlineData("Families.Sos.NotFound", 404)]
    [InlineData("Families.Sos.IdempotencyConflict", 409)]
    [InlineData("Families.Sos.InvalidOperation", 409)]
    [InlineData("Families.Sos.InvalidLocation", 400)]
    public void SosErrors_MapToProblemDetails(string code, int expectedStatus) => Assert.Equal(expectedStatus, ResultProblemDetailsMapper.Create(new Error(code, "test"), new DefaultHttpContext()).Status);

    private static bool Satisfies(AuthorizationPolicy policy, AccountType account, AuthAccessType access) => policy.Requirements.All(x => x switch
    {
        DenyAnonymousAuthorizationRequirement => true,
        ClaimsAuthorizationRequirement claim => claim.AllowedValues?.Contains(claim.ClaimType == AuthClaimNames.AccountType ? account.ToString() : access.ToString()) == true,
        _ => false
    });
}
