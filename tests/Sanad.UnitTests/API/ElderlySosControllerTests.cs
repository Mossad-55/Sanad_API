using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.API.Controllers;
using Sanad.API.ProblemDetail;
using Sanad.BuildingBlocks.Application.Results;

namespace Sanad.UnitTests.API;

public sealed class ElderlySosControllerTests
{
    [Fact]
    public void Controller_UsesElderlyPolicy_AndExposesContractRoutes()
    {
        Assert.Equal(AuthorizationPolicies.ElderlyAccess, Assert.Single(typeof(ElderlySosController).GetCustomAttributes<AuthorizeAttribute>()).Policy);
        Assert.Equal("api/v1/elderly/sos", Assert.Single(typeof(ElderlySosController).GetCustomAttributes<RouteAttribute>()).Template);
        Assert.Equal("{sosId:guid}", typeof(ElderlySosController).GetMethod(nameof(ElderlySosController.Detail))!.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal("{sosId:guid}/cancel", typeof(ElderlySosController).GetMethod(nameof(ElderlySosController.Cancel))!.GetCustomAttribute<HttpPostAttribute>()!.Template);
    }

    [Theory]
    [InlineData("Families.Sos.NotFound", 404)]
    [InlineData("Families.Sos.IdempotencyConflict", 409)]
    [InlineData("Families.Sos.InvalidOperation", 409)]
    [InlineData("Families.Sos.InvalidLocation", 400)]
    public void SosErrors_MapToContractStatus(string code, int expectedStatus)
    {
        Assert.Equal(expectedStatus, ResultProblemDetailsMapper.Create(new Error(code, "test"), new DefaultHttpContext()).Status);
    }
}
