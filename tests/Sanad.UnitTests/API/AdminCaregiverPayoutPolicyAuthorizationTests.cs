using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Sanad.API.Authorization;
using Sanad.API.Controllers;

namespace Sanad.UnitTests.API;

public sealed class AdminCaregiverPayoutPolicyAuthorizationTests
{
    [Fact]
    public void AdminCaregiverPayoutPolicyController_UsesPayoutOperationalAdminPolicy()
    {
        AuthorizeAttribute authorize =
            Assert.Single(
                typeof(AdminCaregiverPayoutPolicyController)
                    .GetCustomAttributes<AuthorizeAttribute>());

        Assert.Equal(
            AuthorizationPolicies.PayoutOperationalAdmin,
            authorize.Policy);

        Assert.Equal(
            "api/v1/admin/finance/caregiver-payout-policies",
            typeof(AdminCaregiverPayoutPolicyController)
                .GetCustomAttributes<RouteAttribute>().Single().Template);
    }

    [Fact]
    public void PayoutPolicyAdminRoutes_HaveExpectedTemplates()
    {
        Assert.Equal(
            "current",
            TemplateOf(nameof(AdminCaregiverPayoutPolicyController.Current)));
        Assert.Equal(
            "history",
            TemplateOf(nameof(AdminCaregiverPayoutPolicyController.History)));
        Assert.NotNull(
            typeof(AdminCaregiverPayoutPolicyController)
                .GetMethod(nameof(AdminCaregiverPayoutPolicyController.Create)));
    }

    private static string? TemplateOf(string methodName) =>
        typeof(AdminCaregiverPayoutPolicyController)
            .GetMethod(methodName)!
            .GetCustomAttributes<HttpMethodAttribute>().Single().Template;
}
