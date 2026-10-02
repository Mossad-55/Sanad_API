using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Sanad.API.Controllers;
using Sanad.API.Authorization;
using Sanad.Modules.Identity.Application.Authentication.Tokens;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.UnitTests.API;

public sealed class AdminCaregiverReviewAuthorizationTests
{
    [Fact]
    public void AdminCaregiversController_UsesCaregiverReviewPolicy()
    {
        AuthorizeAttribute authorize =
            Assert.Single(
                typeof(AdminCaregiversController)
                    .GetCustomAttributes<AuthorizeAttribute>());

        Assert.Equal(
            AuthorizationPolicies.CaregiverReviewAdmin,
            authorize.Policy);
    }

    [Fact]
    public void CaregiverReviewPolicy_RequiresNormalSuperOrSupportAdminClaims()
    {
        AuthorizationPolicy policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireClaim(AuthClaimNames.AccessType, AuthAccessType.Normal.ToString())
            .RequireClaim(
                AuthClaimNames.AccountType,
                AccountType.SuperAdmin.ToString(),
                AccountType.SupportAdmin.ToString())
            .Build();

        Assert.Contains(policy.Requirements, requirement => requirement is DenyAnonymousAuthorizationRequirement);
        ClaimsAuthorizationRequirement access = Assert.Single(policy.Requirements.OfType<ClaimsAuthorizationRequirement>(), requirement => requirement.ClaimType == AuthClaimNames.AccessType);
        Assert.Equal([AuthAccessType.Normal.ToString()], access.AllowedValues);
        ClaimsAuthorizationRequirement account = Assert.Single(policy.Requirements.OfType<ClaimsAuthorizationRequirement>(), requirement => requirement.ClaimType == AuthClaimNames.AccountType);
        Assert.Equal([AccountType.SuperAdmin.ToString(), AccountType.SupportAdmin.ToString()], account.AllowedValues);
    }
}
