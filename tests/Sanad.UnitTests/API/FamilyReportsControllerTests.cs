using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;
using Sanad.API.Authorization;
using Sanad.API.Controllers;

namespace Sanad.UnitTests.API;

public sealed class FamilyReportsControllerTests
{
    [Fact]
    public void Controller_UsesFamilyAccessPolicyAndVisitRoute()
    {
        var authorization = Assert.Single(
            typeof(FamilyReportsController).GetCustomAttributes<AuthorizeAttribute>());
        var route = Assert.Single(
            typeof(FamilyReportsController).GetCustomAttributes<RouteAttribute>());

        Assert.Equal(AuthorizationPolicies.FamilyAccess, authorization.Policy);
        Assert.Equal("api/v1/family/reports", route.Template);
    }

    [Fact]
    public void GetReports_DeclaresActualSuccessResponse()
    {
        var method = typeof(FamilyReportsController).GetMethod(nameof(FamilyReportsController.GetReports));
        Assert.NotNull(method);

        var response = Assert.Single(
            method!.GetCustomAttributes<ProducesResponseTypeAttribute>()
                .Where(attribute => attribute.StatusCode == StatusCodes.Status200OK));

        Assert.Equal(typeof(Sanad.Modules.Families.Application.Reports.PagedVisitReportsResponse), response.Type);
    }
}
