using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;
using Sanad.API.Authorization;
using Sanad.API.Controllers;
using Sanad.Modules.Families.Application.Subscriptions;

namespace Sanad.UnitTests.API;

public sealed class FamilySubscriptionsControllerTests
{
    [Fact]
    public void Uses_family_access_and_owner_read_routes()
    {
        var authorization = Assert.Single(typeof(FamilySubscriptionsController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        var route = Assert.Single(typeof(FamilySubscriptionsController).GetCustomAttributes(typeof(RouteAttribute), true).Cast<RouteAttribute>());
        Assert.Equal(AuthorizationPolicies.FamilyAccess, authorization.Policy);
        Assert.Equal("api/v1/family/subscriptions", route.Template);
        Assert.NotNull(typeof(FamilySubscriptionsController).GetMethod(nameof(FamilySubscriptionsController.GetPlans)));
        Assert.NotNull(typeof(FamilySubscriptionsController).GetMethod(nameof(FamilySubscriptionsController.GetCurrent)));
        Assert.NotNull(typeof(FamilySubscriptionsController).GetMethod(nameof(FamilySubscriptionsController.CancelRenewal)));
        Assert.NotNull(typeof(FamilySubscriptionsController).GetMethod(nameof(FamilySubscriptionsController.ReenableAutoRenew)));
    }

    [Fact]
    public void Current_route_declares_nullable_envelope_success_response()
    {
        var method = typeof(FamilySubscriptionsController).GetMethod(nameof(FamilySubscriptionsController.GetCurrent));
        var response = Assert.Single(method!.GetCustomAttributes<ProducesResponseTypeAttribute>(), x => x.StatusCode == StatusCodes.Status200OK);
        Assert.Equal(typeof(CurrentSubscriptionResponse), response.Type);
    }
}
