using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;
using Sanad.API.Authorization;
using Sanad.API.Controllers;

namespace Sanad.UnitTests.API;

public sealed class MedicationLatenessContractTests
{
    [Fact]
    public void ElderlyLate_UsesElderlyAccessAndApprovedRoute()
    {
        var authorization = Assert.Single(typeof(ElderlyMedicationsController).GetCustomAttributes<AuthorizeAttribute>());
        Assert.Equal(AuthorizationPolicies.ElderlyAccess, authorization.Policy);
        Assert.Equal("late", typeof(ElderlyMedicationsController).GetMethod(nameof(ElderlyMedicationsController.Late))!
            .GetCustomAttribute<HttpGetAttribute>()!.Template);
    }

    [Fact]
    public void AdminEvaluateLate_UsesOperationalReadAndApprovedRoute()
    {
        var authorization = Assert.Single(typeof(AdminElderlyMedicationsController).GetCustomAttributes<AuthorizeAttribute>());
        Assert.Equal(AuthorizationPolicies.ElderlyMedicationOperationalRead, authorization.Policy);
        Assert.Equal("late/evaluate", typeof(AdminElderlyMedicationsController).GetMethod(nameof(AdminElderlyMedicationsController.EvaluateLate))!
            .GetCustomAttribute<HttpPostAttribute>()!.Template);
    }

    [Fact]
    public void CmsSettingRoutes_RequireCmsContent()
    {
        var authorization = Assert.Single(typeof(AdminMedicationLatenessController).GetCustomAttributes<AuthorizeAttribute>());
        Assert.Equal(AuthorizationPolicies.CmsContent, authorization.Policy);
        Assert.Equal("api/v1/admin/cms/medication-lateness", typeof(AdminMedicationLatenessController)
            .GetCustomAttribute<RouteAttribute>()!.Template);
    }
}
