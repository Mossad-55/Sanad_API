using Microsoft.AspNetCore.Http;
using Ahlena.API.ProblemDetail;
using Ahlena.BuildingBlocks.Application.Results;

namespace Ahlena.UnitTests.API;

public sealed class AccountErrorMappingTests
{
    [Theory]
    [InlineData("Identity.Account.ElderlyManagedByFamily", StatusCodes.Status409Conflict)]
    [InlineData("Identity.Account.OwnershipTransferRequired", StatusCodes.Status409Conflict)]
    public void Create_ShouldMapNewAccountErrors_To409(
        string errorCode,
        int expectedStatus)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.TraceIdentifier = "trace-id";
        httpContext.Request.Path = "/api/v1/account";

        var problemDetails = ResultProblemDetailsMapper.Create(
            new Error(errorCode, "Internal must not leak"),
            httpContext);

        Assert.Equal(expectedStatus, problemDetails.Status);
        Assert.Equal(errorCode, problemDetails.Extensions["code"]);
    }
}
