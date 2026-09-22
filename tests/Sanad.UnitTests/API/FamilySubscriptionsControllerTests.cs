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
        Assert.NotNull(typeof(FamilySubscriptionsController).GetMethod(nameof(FamilySubscriptionsController.Quote)));
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

    [Fact]
    public void Quote_route_declares_expected_success_and_error_responses()
    {
        var method = typeof(FamilySubscriptionsController).GetMethod(nameof(FamilySubscriptionsController.Quote));
        var route = Assert.Single(method!.GetCustomAttributes<HttpPostAttribute>());
        var responses = method!.GetCustomAttributes<ProducesResponseTypeAttribute>()
            .ToDictionary(attribute => attribute.StatusCode);

        Assert.Equal("quote", route.Template);
        Assert.Equal(typeof(SubscriptionQuoteResponse), responses[StatusCodes.Status200OK].Type);
        Assert.Contains(StatusCodes.Status400BadRequest, responses.Keys);
        Assert.Contains(StatusCodes.Status403Forbidden, responses.Keys);
        Assert.Contains(StatusCodes.Status404NotFound, responses.Keys);
        Assert.Contains(StatusCodes.Status409Conflict, responses.Keys);
    }
}
