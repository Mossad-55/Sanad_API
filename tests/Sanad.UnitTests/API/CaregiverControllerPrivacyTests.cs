using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.API.Controllers;

namespace Sanad.UnitTests.API;

public sealed class CaregiverControllerPrivacyTests
{
    [Fact]
    public void CaregiverController_ShouldHaveCaregiverAccessPolicy()
    {
        var controllerType = typeof(CaregiverController);
        var authorize = controllerType.GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal(AuthorizationPolicies.CaregiverAccess, authorize.Policy);
    }

    [Fact]
    public void GetPrivacy_ShouldHaveHttpGetAttribute()
    {
        var method = typeof(CaregiverController).GetMethod(nameof(CaregiverController.GetPrivacy));
        Assert.NotNull(method);

        var httpGet = method.GetCustomAttribute<HttpGetAttribute>();
        Assert.NotNull(httpGet);
        Assert.Equal("privacy", httpGet.Template);
    }

    [Fact]
    public void UpdatePrivacy_ShouldHaveHttpPutAttribute()
    {
        var method = typeof(CaregiverController).GetMethod(nameof(CaregiverController.UpdatePrivacy));
        Assert.NotNull(method);

        var httpPut = method.GetCustomAttribute<HttpPutAttribute>();
        Assert.NotNull(httpPut);
        Assert.Equal("privacy", httpPut.Template);
    }

    [Fact]
    public void GetPrivacy_ShouldReturnPrivacyResponse()
    {
        var method = typeof(CaregiverController).GetMethod(nameof(CaregiverController.GetPrivacy));
        Assert.NotNull(method);

        var produces = method.GetCustomAttributes<ProducesResponseTypeAttribute>()
            .FirstOrDefault(a => a.StatusCode == StatusCodes.Status200OK);

        Assert.NotNull(produces);
    }

    [Fact]
    public void UpdatePrivacy_ShouldReturn204()
    {
        var method = typeof(CaregiverController).GetMethod(nameof(CaregiverController.UpdatePrivacy));
        Assert.NotNull(method);

        var produces = method.GetCustomAttributes<ProducesResponseTypeAttribute>()
            .FirstOrDefault(a => a.StatusCode == StatusCodes.Status204NoContent);

        Assert.NotNull(produces);
    }
}
