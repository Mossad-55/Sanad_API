using Microsoft.AspNetCore.Http;
using Sanad.API.ProblemDetail;
using Sanad.BuildingBlocks.Application.Results;

namespace Sanad.UnitTests.API;

public sealed class ResultProblemDetailsMapperTests
{
    [Theory]
    [InlineData(
        "Identity.Login.InvalidCredentials",
        StatusCodes.Status401Unauthorized,
        "Authentication failed.")]
    [InlineData(
        "Identity.Login.UserSuspended",
        StatusCodes.Status403Forbidden,
        "The requested operation is not allowed.")]
    [InlineData(
        "Identity.Sessions.SessionNotFound",
        StatusCodes.Status404NotFound,
        "The requested resource was not found.")]
    [InlineData(
        "Identity.Registration.EmailAlreadyInUse",
        StatusCodes.Status409Conflict,
        "The request conflicts with the current state.")]
    [InlineData(
        "Identity.Registration.UnsupportedAccountType",
        StatusCodes.Status400BadRequest,
        "The request could not be completed.")]
    [InlineData(
        "Caregivers.Lookups.NameAlreadyInUse",
        StatusCodes.Status409Conflict,
        "The request conflicts with the current state.")]
    [InlineData(
        "Caregivers.Lookups.NotFound",
        StatusCodes.Status404NotFound,
        "The requested resource was not found.")]
    [InlineData(
        "Caregivers.Lookups.LanguageCodeInUse",
        StatusCodes.Status409Conflict,
        "The request conflicts with the current state.")]
    public void Create_ShouldMapStableErrorCode(
        string errorCode,
        int expectedStatusCode,
        string expectedDetail)
    {
        var httpContext =
            new DefaultHttpContext();

        httpContext.TraceIdentifier =
            "trace-id";

        httpContext.Request.Path =
            "/api/v1/auth/test";

        var problemDetails =
            ResultProblemDetailsMapper.Create(
                new Error(
                    errorCode,
                    "Internal message must not leak."),
                httpContext);

        Assert.Equal(
            expectedStatusCode,
            problemDetails.Status);

        Assert.Equal(
            expectedDetail,
            problemDetails.Detail);

        Assert.Equal(
            errorCode,
            problemDetails.Extensions["code"]);

        Assert.Equal(
            "trace-id",
            problemDetails.Extensions["traceId"]);

        Assert.DoesNotContain(
            "Internal message",
            problemDetails.Detail);
    }

    [Fact]
    public void Create_ShouldUseBadRequestForUnknownErrorCode()
    {
        var problemDetails =
            ResultProblemDetailsMapper.Create(
                new Error(
                    "Unknown.Error",
                    "Internal message"),
                new DefaultHttpContext());

        Assert.Equal(
            StatusCodes.Status400BadRequest,
            problemDetails.Status);

        Assert.Equal(
            "Unknown.Error",
            problemDetails.Extensions["code"]);
    }
}