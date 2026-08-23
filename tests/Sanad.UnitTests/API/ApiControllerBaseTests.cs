using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Controllers;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.UnitTests.API;

public sealed class ApiControllerBaseTests
{
    [Fact]
    public void TryGetAuthenticatedUserId_ShouldReturnTrue_ForValidSubjectClaim()
    {
        UserId expectedUserId = UserId.New();
        var controller = CreateController();

        controller.ControllerContext.HttpContext.User =
            new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(
                        JwtRegisteredClaimNames.Sub,
                        expectedUserId.Value.ToString())
                ],
                "test"));

        bool result = controller.ReadUserId(
            out UserId actualUserId);

        Assert.True(result);
        Assert.Equal(expectedUserId, actualUserId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void TryGetAuthenticatedUserId_ShouldReturnFalse_ForMissingOrInvalidSubject(
        string? subject)
    {
        var controller = CreateController();

        if (subject is not null)
        {
            controller.ControllerContext.HttpContext.User =
                new ClaimsPrincipal(
                    new ClaimsIdentity(
                    [
                        new Claim(
                            JwtRegisteredClaimNames.Sub,
                            subject)
                    ],
                    "test"));
        }

        bool result = controller.ReadUserId(
            out UserId userId);

        Assert.False(result);
        Assert.Equal(UserId.Empty, userId);
    }

    [Fact]
    public void TryGetCurrentDeviceSessionId_ShouldReturnTrue_ForValidHeader()
    {
        DeviceSessionId expectedSessionId =
            DeviceSessionId.New();

        var controller = CreateController();

        controller.ControllerContext.HttpContext.Request.Headers[
            "X-Device-Session-Id"] =
            expectedSessionId.Value.ToString();

        bool result = controller.ReadSessionId(
            out DeviceSessionId actualSessionId);

        Assert.True(result);
        Assert.Equal(expectedSessionId, actualSessionId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void TryGetCurrentDeviceSessionId_ShouldReturnFalse_ForMissingOrInvalidHeader(
        string? headerValue)
    {
        var controller = CreateController();

        if (headerValue is not null)
        {
            controller.ControllerContext.HttpContext.Request.Headers[
                "X-Device-Session-Id"] =
                headerValue;
        }

        bool result = controller.ReadSessionId(
            out DeviceSessionId sessionId);

        Assert.False(result);
        Assert.Equal(DeviceSessionId.Empty, sessionId);
    }

    [Fact]
    public void BadRequestWithCode_ShouldReturnSafeProblemDetails()
    {
        var controller = CreateController();

        controller.ControllerContext.HttpContext.TraceIdentifier =
            "trace-id";

        controller.ControllerContext.HttpContext.Request.Path =
            "/api/v1/auth/test";

        IActionResult result = controller.CreateBadRequest(
            "Api.Auth.InvalidDeviceSessionHeader");

        var badRequest = Assert.IsType<BadRequestObjectResult>(
            result);

        var problemDetails = Assert.IsType<ProblemDetails>(
            badRequest.Value);

        Assert.Equal(
            StatusCodes.Status400BadRequest,
            problemDetails.Status);

        Assert.Equal(
            "Api.Auth.InvalidDeviceSessionHeader",
            problemDetails.Extensions["code"]);

        Assert.Equal(
            "trace-id",
            problemDetails.Extensions["traceId"]);

        Assert.Equal(
            "The request could not be completed.",
            problemDetails.Detail);
    }

    private static TestController CreateController()
    {
        return new TestController
        {
            ControllerContext =
                new ControllerContext
                {
                    HttpContext = new DefaultHttpContext()
                }
        };
    }

    private sealed class TestController :
        ApiControllerBase
    {
        public bool ReadUserId(
            out UserId userId)
        {

            return TryGetAuthenticatedUserId(
                out userId);
        }

        public bool ReadSessionId(
            out DeviceSessionId sessionId)
        {
            return TryGetCurrentDeviceSessionId(
                out sessionId);
        }

        public IActionResult CreateBadRequest(
            string code)
        {
            return BadRequestWithCode(code);
        }
    }
}
