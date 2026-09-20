using Microsoft.AspNetCore.Mvc;
using Sanad.API.Controllers;

namespace Sanad.UnitTests.API;

public sealed class MedicalReportControllerTests
{
    [Fact]
    public void FamilyPhotoEndpointIsDeclared()
    {
        var method = typeof(FamilyReportsController).GetMethod(nameof(FamilyReportsController.ReadMedicalReportPhoto));
        Assert.NotNull(method);
        Assert.NotEmpty(
            method!.GetCustomAttributes(
                typeof(HttpGetAttribute),
                inherit: true));
    }
}
