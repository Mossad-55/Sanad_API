using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Controllers;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Cms.Application.Legal;
using Sanad.Modules.Cms.Domain.Legal;
using Sanad.Modules.Identity.Application.Authentication.Tokens;
using Sanad.Modules.Identity.Domain.Users;

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

    [Fact]
    public void GetOwnedContentAudiences_ShouldBeOrderIndependentAndCollapseDuplicates()
    {
        TestController ordered =
            CreateClaimController(
                AccountType.Family,
                AccountType.CareHomeOwner);
        TestController reversed =
            CreateClaimController(
                AccountType.CareHomeOwner,
                AccountType.Family);
        TestController duplicated =
            CreateClaimController(
                AccountType.Family,
                AccountType.Family);

        Assert.Equal(
            new[] { LegalAudience.Family, LegalAudience.CareHomeOwner },
            ordered.ReadOwnedAudiences());
        Assert.Equal(
            ordered.ReadOwnedAudiences(),
            reversed.ReadOwnedAudiences());
        Assert.Equal(
            new[] { LegalAudience.Family },
            duplicated.ReadOwnedAudiences());
    }

    [Fact]
    public void GetOwnedContentAudiences_ShouldMapOwnerByValueSpaceAndExcludeAdmins()
    {
        TestController owner =
            CreateClaimController(
                AccountType.CareHomeOwner);

        Assert.Equal(
            new[] { LegalAudience.CareHomeOwner },
            owner.ReadOwnedAudiences());

        // AccountType.CareHomeOwner is 8 and LegalAudience.CareHomeOwner is 5;
        // the two value spaces must never be cast into one another.
        Assert.Equal(5, (int)LegalAudience.CareHomeOwner);
        Assert.Equal(8, (int)AccountType.CareHomeOwner);
        Assert.DoesNotContain(
            (LegalAudience)(int)AccountType.CareHomeOwner,
            owner.ReadOwnedAudiences());

        TestController admins =
            CreateClaimController(
                AccountType.SuperAdmin,
                AccountType.ContentAdmin,
                AccountType.SupportAdmin,
                AccountType.FinanceAdmin);

        Assert.Empty(admins.ReadOwnedAudiences());
    }

    [Theory]
    [InlineData("1", LegalAudience.Family)]
    [InlineData("5", LegalAudience.CareHomeOwner)]
    [InlineData("Family", LegalAudience.Family)]
    [InlineData("carehomeowner", LegalAudience.CareHomeOwner)]
    [InlineData(" 5 ", LegalAudience.CareHomeOwner)]
    public void TryParseContentAudience_ShouldAcceptSingleValue(
        string value,
        LegalAudience expected)
    {
        TestController controller = CreateController();

        Assert.True(
            controller.ParseAudience(
                value,
                out LegalAudience audience));
        Assert.Equal(expected, audience);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0")]
    [InlineData("6")]
    [InlineData("9")]
    [InlineData("abc")]
    [InlineData("1,2")]
    [InlineData("Family,CareHomeOwner")]
    public void TryParseContentAudience_ShouldRejectMissingMalformedOrUndefined(
        string? value)
    {
        TestController controller = CreateController();

        Assert.False(
            controller.ParseAudience(
                value,
                out LegalAudience audience));
        Assert.Equal(default(LegalAudience), audience);
    }

    [Fact]
    public void ResolveContentAudience_ShouldSelectSingleOwnedAudienceWhenOmitted()
    {
        TestController controller =
            CreateClaimController(AccountType.Family);

        Assert.True(
            controller.ResolveAudience(
                null,
                out LegalAudience audience,
                out IActionResult? failure));

        Assert.Equal(LegalAudience.Family, audience);
        Assert.Null(failure);
    }

    [Fact]
    public void ResolveContentAudience_ShouldAcceptOwnedAudienceRegardlessOfClaimOrder()
    {
        TestController forward =
            CreateClaimController(
                AccountType.CareHomeOwner,
                AccountType.Family);
        TestController backward =
            CreateClaimController(
                AccountType.Family,
                AccountType.CareHomeOwner);

        foreach (TestController controller in
            new[] { forward, backward })
        {
            Assert.True(
                controller.ResolveAudience(
                    "5",
                    out LegalAudience audience,
                    out IActionResult? failure));

            Assert.Equal(LegalAudience.CareHomeOwner, audience);
            Assert.Null(failure);
        }
    }

    [Fact]
    public void ResolveContentAudience_ShouldRequireAudienceWhenMultipleOwned()
    {
        TestController controller =
            CreateClaimController(
                AccountType.Family,
                AccountType.CareHomeOwner);

        Assert.False(
            controller.ResolveAudience(
                null,
                out _,
                out IActionResult? failure));

        Assert.Equal(
            "Cms.Content.AudienceRequired",
            FailureCode(failure));
        Assert.Equal(
            StatusCodes.Status400BadRequest,
            Assert.IsType<BadRequestObjectResult>(
                failure).StatusCode);
    }

    [Fact]
    public void ResolveContentAudience_ShouldRejectMalformedAudienceAsCodedBadRequest()
    {
        TestController controller =
            CreateClaimController(
                AccountType.Family,
                AccountType.CareHomeOwner);

        Assert.False(
            controller.ResolveAudience(
                "9",
                out _,
                out IActionResult? failure));

        Assert.Equal(
            "Cms.Content.InvalidAudience",
            FailureCode(failure));
        Assert.Equal(
            StatusCodes.Status400BadRequest,
            Assert.IsType<BadRequestObjectResult>(
                failure).StatusCode);
    }

    [Fact]
    public void ResolveContentAudience_ShouldDenyValidButUnownedAudience()
    {
        TestController controller =
            CreateClaimController(AccountType.Family);

        Assert.False(
            controller.ResolveAudience(
                "5",
                out _,
                out IActionResult? failure));

        Assert.Equal(
            ContentErrors.UnsupportedAudience.Code,
            FailureCode(failure));
        Assert.Equal(
            StatusCodes.Status403Forbidden,
            Assert.IsType<ObjectResult>(
                failure).StatusCode);
    }

    [Fact]
    public void ResolveContentAudience_ShouldPreserveAdminDenialPatternWhenOmitted()
    {
        TestController controller =
            CreateClaimController(AccountType.SupportAdmin);

        Assert.False(
            controller.ResolveAudience(
                null,
                out _,
                out IActionResult? failure));

        Assert.Equal(
            ContentErrors.UnsupportedAudience.Code,
            FailureCode(failure));
        Assert.Equal(
            StatusCodes.Status403Forbidden,
            Assert.IsType<ObjectResult>(
                failure).StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveContentAudience_ShouldTreatEmptyOrWhitespaceAsOmitted_ForSingleOwnedAudience(
        string requestedAudience)
    {
        TestController controller =
            CreateClaimController(AccountType.CareHomeOwner);

        Assert.True(
            controller.ResolveAudience(
                requestedAudience,
                out LegalAudience audience,
                out IActionResult? failure));

        Assert.Equal(LegalAudience.CareHomeOwner, audience);
        Assert.Null(failure);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveContentAudience_ShouldTreatEmptyOrWhitespaceAsOmitted_ForMultipleOwnedAudiences(
        string requestedAudience)
    {
        TestController controller =
            CreateClaimController(
                AccountType.Family,
                AccountType.CareHomeOwner);

        Assert.False(
            controller.ResolveAudience(
                requestedAudience,
                out _,
                out IActionResult? failure));

        Assert.Equal(
            "Cms.Content.AudienceRequired",
            FailureCode(failure));
    }

    [Fact]
    public void ResolveContentAudience_ShouldReportMalformedAudienceBeforeAdminDenial()
    {
        // Malformed input is a 400 for every account class; the admin 403
        // denial only applies once the value is well-formed or omitted.
        TestController controller =
            CreateClaimController(AccountType.SupportAdmin);

        Assert.False(
            controller.ResolveAudience(
                "abc",
                out _,
                out IActionResult? failure));

        Assert.Equal(
            "Cms.Content.InvalidAudience",
            FailureCode(failure));
        Assert.Equal(
            StatusCodes.Status400BadRequest,
            Assert.IsType<BadRequestObjectResult>(
                failure).StatusCode);
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

    private static TestController CreateClaimController(
        params AccountType[] accountTypes)
    {
        IEnumerable<Claim> claims =
            accountTypes.Select(
                accountType =>
                    new Claim(
                        AuthClaimNames.AccountType,
                        accountType.ToString()));

        return new TestController
        {
            ControllerContext =
                new ControllerContext
                {
                    HttpContext =
                        new DefaultHttpContext
                        {
                            User =
                                new ClaimsPrincipal(
                                    new ClaimsIdentity(
                                        claims,
                                        "test"))
                        }
                }
        };
    }

    private static string? FailureCode(
        IActionResult? failure)
    {
        ObjectResult result =
            Assert.IsAssignableFrom<ObjectResult>(failure);

        ProblemDetails problem =
            Assert.IsType<ProblemDetails>(result.Value);

        return problem.Extensions["code"]?.ToString();
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

        public IReadOnlyList<LegalAudience> ReadOwnedAudiences()
        {
            return GetOwnedContentAudiences();
        }

        public bool ResolveAudience(
            string? requestedAudience,
            out LegalAudience audience,
            out IActionResult? failure)
        {
            return TryResolveContentAudience(
                requestedAudience,
                out audience,
                out failure);
        }

        public bool ParseAudience(
            string? value,
            out LegalAudience audience)
        {
            return TryParseContentAudience(
                value,
                out audience);
        }
    }
}
