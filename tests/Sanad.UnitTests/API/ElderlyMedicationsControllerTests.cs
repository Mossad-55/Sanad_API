using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.API.Controllers;
using Xunit;

namespace Sanad.UnitTests.API;

public sealed class ElderlyMedicationsControllerTests
{
    [Fact]
    public async Task Dashboard_WithoutExplicitDate_ReturnsBadRequestBeforeSendingQuery()
    {
        var controller = new ElderlyMedicationsController(null!)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString())
                    }, "test"))
                }
            }
        };

        var result = await controller.Dashboard(null, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public void Controller_RequiresElderlyAccessPolicy()
    {
        var attribute = Assert.Single(typeof(ElderlyMedicationsController)
            .GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true)
            .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>());

        Assert.Equal(AuthorizationPolicies.ElderlyAccess, attribute.Policy);
    }
}
