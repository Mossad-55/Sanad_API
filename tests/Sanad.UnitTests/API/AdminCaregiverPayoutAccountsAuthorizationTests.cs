using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Sanad.API.Authorization;
using Sanad.API.Controllers;

namespace Sanad.UnitTests.API;

public sealed class AdminCaregiverPayoutAccountsAuthorizationTests
{
    [Fact]
    public void AdminCaregiverPayoutAccountsController_UsesPayoutOperationalAdminPolicy()
    {
        AuthorizeAttribute authorize =
            Assert.Single(
                typeof(AdminCaregiverPayoutAccountsController)
                    .GetCustomAttributes<AuthorizeAttribute>());

        Assert.Equal(
            AuthorizationPolicies.PayoutOperationalAdmin,
            authorize.Policy);

        Assert.Equal(
            "api/v1/admin/caregiver-payout-accounts",
            typeof(AdminCaregiverPayoutAccountsController)
                .GetCustomAttributes<RouteAttribute>().Single().Template);
    }

    [Fact]
    public void PayoutAccountReviewRoutes_HaveExpectedTemplates()
    {
        Assert.Null(TemplateOf(nameof(AdminCaregiverPayoutAccountsController.GetPayoutAccounts)));
        Assert.Equal(
            "{caregiverId:guid}",
            TemplateOf(nameof(AdminCaregiverPayoutAccountsController.GetPayoutAccount)));
        Assert.Equal(
            "{caregiverId:guid}/reviews",
            TemplateOf(nameof(AdminCaregiverPayoutAccountsController.GetReviews)));
        Assert.Equal(
            "{caregiverId:guid}/approve",
            TemplateOf(nameof(AdminCaregiverPayoutAccountsController.Approve)));
        Assert.Equal(
            "{caregiverId:guid}/reject",
            TemplateOf(nameof(AdminCaregiverPayoutAccountsController.Reject)));
        Assert.Equal(
            "{caregiverId:guid}/revoke",
            TemplateOf(nameof(AdminCaregiverPayoutAccountsController.Revoke)));
        Assert.Equal(
            "{caregiverId:guid}/reveal",
            TemplateOf(nameof(AdminCaregiverPayoutAccountsController.Reveal)));
    }

    private static string? TemplateOf(string methodName) =>
        typeof(AdminCaregiverPayoutAccountsController)
            .GetMethod(methodName)!
            .GetCustomAttributes<HttpMethodAttribute>().Single().Template;
}
