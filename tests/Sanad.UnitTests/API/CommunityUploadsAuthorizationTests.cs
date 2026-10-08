using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Sanad.API.Controllers;

namespace Sanad.UnitTests.API;

public sealed class CommunityUploadsAuthorizationTests
{
    [Fact]
    public void CommunityUploadsController_RequiresAuthenticatedUser()
    {
        AuthorizeAttribute authorize =
            Assert.Single(
                typeof(CommunityUploadsController)
                    .GetCustomAttributes<AuthorizeAttribute>());

        Assert.Null(authorize.Policy);

        Assert.Equal(
            "api/v1/community/uploads",
            typeof(CommunityUploadsController)
                .GetCustomAttributes<RouteAttribute>().Single().Template);

        Assert.Equal(
            "images",
            typeof(CommunityUploadsController)
                .GetMethod(nameof(CommunityUploadsController.UploadImage))!
                .GetCustomAttributes<HttpMethodAttribute>().Single().Template);
    }
}
