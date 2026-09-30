using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.API.Controllers;
using Sanad.API.ProblemDetail;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Families.Domain.HelpRequests;
using Sanad.Modules.Identity.Application.Authentication.Tokens;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.UnitTests.API;

public sealed class AdminElderlyHelpRequestControllerTests
{
    [Fact]
    public void Controller_UsesOperationalPolicy_AndExposesReadAndTransitionRoutes()
    {
        Assert.Equal(AuthorizationPolicies.ElderlyHelpRequestOperational, Assert.Single(typeof(AdminElderlyHelpRequestsController).GetCustomAttributes<AuthorizeAttribute>()).Policy);
        Assert.Equal("api/v1/admin/elderly/help-requests", Assert.Single(typeof(AdminElderlyHelpRequestsController).GetCustomAttributes<RouteAttribute>()).Template);
        Assert.Equal("{requestId:guid}/history", typeof(AdminElderlyHelpRequestsController).GetMethod(nameof(AdminElderlyHelpRequestsController.History))!.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal("aggregate", typeof(AdminElderlyHelpRequestsController).GetMethod(nameof(AdminElderlyHelpRequestsController.Aggregate))!.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal("{requestId:guid}/{statusAction}", typeof(AdminElderlyHelpRequestsController).GetMethod(nameof(AdminElderlyHelpRequestsController.Change))!.GetCustomAttribute<HttpPostAttribute>()!.Template);
    }

    [Fact]
    public void OperationalPolicy_AllowsOnlyNormalSupportOrSuperAdmin()
    {
        var policy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser()
            .RequireClaim(AuthClaimNames.AccessType, AuthAccessType.Normal.ToString())
            .RequireClaim(AuthClaimNames.AccountType, AccountType.SupportAdmin.ToString(), AccountType.SuperAdmin.ToString()).Build();
        Assert.True(Satisfies(policy, AccountType.SupportAdmin, AuthAccessType.Normal));
        Assert.True(Satisfies(policy, AccountType.SuperAdmin, AuthAccessType.Normal));
        Assert.False(Satisfies(policy, AccountType.ContentAdmin, AuthAccessType.Normal));
        Assert.False(Satisfies(policy, AccountType.Family, AuthAccessType.Normal));
        Assert.False(Satisfies(policy, AccountType.SupportAdmin, AuthAccessType.RestrictedVerification));
    }

    [Theory]
    [InlineData("Families.HelpRequest.NotFound", 404)]
    [InlineData("Families.HelpRequest.IdempotencyConflict", 409)]
    [InlineData("Families.HelpRequest.InvalidOperation", 409)]
    [InlineData("Cms.SentenceBuilder.NotFound", 404)]
    [InlineData("Cms.SentenceBuilder.InvalidOperation", 409)]
    public void NewHelpRequestErrors_MapToContractStatus(string code, int expectedStatus)
    {
        var result = Result.Failure(new Error(code, "test"));
        var problem = ResultProblemDetailsMapper.Create(result.Error, new DefaultHttpContext());

        Assert.Equal(expectedStatus, problem.Status);
    }

    private static bool Satisfies(AuthorizationPolicy policy, AccountType account, AuthAccessType access) => policy.Requirements.All(x => x switch
    {
        DenyAnonymousAuthorizationRequirement => true,
        ClaimsAuthorizationRequirement claim => claim.AllowedValues?.Contains(claim.ClaimType == AuthClaimNames.AccountType ? account.ToString() : access.ToString()) == true,
        _ => false
    });
}
