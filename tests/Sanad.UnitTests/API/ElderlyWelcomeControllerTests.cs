using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Sanad.API.Authorization;
using Sanad.API.Controllers;
using System.Reflection;

namespace Sanad.UnitTests.API;

public sealed class ElderlyWelcomeControllerTests
{
    [Fact]
    public void AdminController_RequiresCmsContentPolicyForEveryAction()
    {
        var controller = typeof(AdminElderlyWelcomeController);
        var authorize = Assert.Single(controller.GetCustomAttributes<AuthorizeAttribute>());
        Assert.Equal(AuthorizationPolicies.CmsContent, authorize.Policy);
        Assert.Equal("api/v1/admin/cms/elderly-welcome", controller.GetCustomAttribute<RouteAttribute>()!.Template);

        AssertAction<HttpGetAttribute>(nameof(AdminElderlyWelcomeController.Get), null);
        AssertAction<HttpPostAttribute>(nameof(AdminElderlyWelcomeController.Create), null);
        AssertAction<HttpPutAttribute>(nameof(AdminElderlyWelcomeController.Update), null);
        AssertAction<HttpPostAttribute>(nameof(AdminElderlyWelcomeController.Publish), "publish");
        AssertAction<HttpPostAttribute>(nameof(AdminElderlyWelcomeController.Unpublish), "unpublish");
    }

    [Fact]
    public void PublicController_AllowsAnonymousAccessOnExpectedRoute()
    {
        var controller = typeof(ElderlyWelcomeController);
        Assert.NotEmpty(controller.GetCustomAttributes<AllowAnonymousAttribute>());
        Assert.Equal("api/v1/elderly/welcome", controller.GetCustomAttribute<RouteAttribute>()!.Template);
    }

    private static void AssertAction<THttpAttribute>(string methodName, string? expectedTemplate)
        where THttpAttribute : HttpMethodAttribute
    {
        var method = typeof(AdminElderlyWelcomeController).GetMethod(methodName)!;
        var attribute = Assert.Single(method.GetCustomAttributes<THttpAttribute>());
        Assert.Equal(expectedTemplate, attribute.Template);
    }
}
