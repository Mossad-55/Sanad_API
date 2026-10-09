using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Sanad.API.Authorization;
using Sanad.API.Controllers;

namespace Sanad.UnitTests.API;

public sealed class AdminCaregiverPayoutsAuthorizationTests
{
    [Fact]
    public void AdminCaregiverPayoutsController_UsesPayoutOperationalAdminPolicy()
    {
        AuthorizeAttribute authorize =
            Assert.Single(
                typeof(AdminCaregiverPayoutsController)
                    .GetCustomAttributes<AuthorizeAttribute>());

        Assert.Equal(
            AuthorizationPolicies.PayoutOperationalAdmin,
            authorize.Policy);

        Assert.Equal(
            "api/v1/admin/caregiver-payouts",
            typeof(AdminCaregiverPayoutsController)
                .GetCustomAttributes<RouteAttribute>().Single().Template);
    }

    [Fact]
    public void CaregiverPayoutAdminRoutes_HaveExpectedTemplates()
    {
        Assert.Equal(
            "{payoutId:guid}",
            TemplateOf(nameof(AdminCaregiverPayoutsController.GetPayout)));
        Assert.Equal(
            "record",
            TemplateOf(nameof(AdminCaregiverPayoutsController.Record)));
        Assert.Equal(
            "{payoutId:guid}/mark-failed",
            TemplateOf(nameof(AdminCaregiverPayoutsController.MarkFailed)));
        Assert.NotNull(
            typeof(AdminCaregiverPayoutsController)
                .GetMethod(nameof(AdminCaregiverPayoutsController.GetPayouts)));
    }

    private static string? TemplateOf(string methodName) =>
        typeof(AdminCaregiverPayoutsController)
            .GetMethod(methodName)!
            .GetCustomAttributes<HttpMethodAttribute>().Single().Template;
}
