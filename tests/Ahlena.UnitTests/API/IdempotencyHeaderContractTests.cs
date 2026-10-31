using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using Ahlena.API.Controllers;
using Ahlena.API.ProblemDetail;
using Ahlena.BuildingBlocks.Application.Idempotency;
using Ahlena.BuildingBlocks.Application.Results;

namespace Ahlena.UnitTests.API;

/// <summary>
/// Controller-level contract for the optional <c>Idempotency-Key</c> header and
/// the replay helpers: one parse per request, a coded 400 for a malformed or
/// repeated header, and a stored byte-exact body replayed with its stored
/// status for a confirmed retry.
/// </summary>
public sealed class IdempotencyHeaderContractTests
{
    private const string InvalidCode = "Bookings.IdempotencyKeyInvalid";

    private sealed class ProbeController : ApiControllerBase
    {
        public bool Read(
            string invalidCode,
            out IdempotencyKey? key,
            out IActionResult? failure) =>
            TryGetIdempotencyKey(invalidCode, out key, out failure);

        public IActionResult OkOutcome<T>(Result<IdempotentOutcome<T>> result) =>
            IdempotentOk(result);

        public IActionResult CreatedOutcome<T>(Result<IdempotentOutcome<T>> result) =>
            IdempotentCreated(result);
    }

    private static ProbeController Controller(IDictionary<string, StringValues>? headers = null)
    {
        var httpContext = new DefaultHttpContext();

        if (headers is not null)
        {
            foreach ((string name, StringValues value) in headers)
            {
                httpContext.Request.Headers[name] = value;
            }
        }

        return new ProbeController
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            }
        };
    }

    [Fact]
    public void Header_Absent_IsAcceptedWithNoKey()
    {
        bool accepted = Controller().Read(InvalidCode, out IdempotencyKey? key, out IActionResult? failure);

        Assert.True(accepted);
        Assert.Null(key);
        Assert.Null(failure);
    }

    [Fact]
    public void Header_WhitespaceOnly_IsAcceptedWithNoKey()
    {
        var headers = new Dictionary<string, StringValues>
        {
            ["Idempotency-Key"] = "   "
        };

        bool accepted = Controller(headers).Read(InvalidCode, out IdempotencyKey? key, out IActionResult? failure);

        Assert.True(accepted);
        Assert.Null(key);
        Assert.Null(failure);
    }

    [Fact]
    public void Header_Valid_IsTrimmedAndReturned()
    {
        var headers = new Dictionary<string, StringValues>
        {
            ["Idempotency-Key"] = "  checkout-1  "
        };

        bool accepted = Controller(headers).Read(InvalidCode, out IdempotencyKey? key, out IActionResult? failure);

        Assert.True(accepted);
        Assert.NotNull(key);
        Assert.Equal("checkout-1", key!.Value);
        Assert.Null(failure);
    }

    [Fact]
    public void Header_Repeated_IsRefusedWithTheRouteCode()
    {
        var headers = new Dictionary<string, StringValues>
        {
            ["Idempotency-Key"] = new StringValues(["first", "second"])
        };

        bool accepted = Controller(headers).Read(InvalidCode, out IdempotencyKey? key, out IActionResult? failure);

        Assert.False(accepted);
        Assert.Null(key);
        AssertCodedBadRequest(failure, InvalidCode);
    }

    [Theory]
    [InlineData("has\ttab")]
    [InlineData("non-ascii-é")]
    public void Header_Malformed_IsRefusedWithTheRouteCode(string raw)
    {
        var headers = new Dictionary<string, StringValues>
        {
            ["Idempotency-Key"] = raw
        };

        bool accepted = Controller(headers).Read(InvalidCode, out IdempotencyKey? key, out IActionResult? failure);

        Assert.False(accepted);
        Assert.Null(key);
        AssertCodedBadRequest(failure, InvalidCode);
    }

    [Fact]
    public void Header_Overlong_IsRefused()
    {
        var headers = new Dictionary<string, StringValues>
        {
            ["Idempotency-Key"] = new string('a', IdempotencyKey.MaximumLength + 1)
        };

        bool accepted = Controller(headers).Read(InvalidCode, out _, out IActionResult? failure);

        Assert.False(accepted);
        AssertCodedBadRequest(failure, InvalidCode);
    }

    [Fact]
    public void IdempotentOk_FreshOutcome_KeepsTheNormalSuccessShape()
    {
        var result = Result<IdempotentOutcome<string>>.Success(
            IdempotentOutcome<string>.Fresh("fresh-body", 200));

        IActionResult action = Controller().OkOutcome(result);

        var objectResult = Assert.IsType<ObjectResult>(action);
        Assert.Equal(StatusCodes.Status200OK, objectResult.StatusCode);
        Assert.Equal("fresh-body", objectResult.Value);
    }

    [Fact]
    public void IdempotentCreated_FreshOutcome_IsA201()
    {
        var result = Result<IdempotentOutcome<string>>.Success(
            IdempotentOutcome<string>.Fresh("created-body", 201));

        IActionResult action = Controller().CreatedOutcome(result);

        var objectResult = Assert.IsType<ObjectResult>(action);
        Assert.Equal(StatusCodes.Status201Created, objectResult.StatusCode);
    }

    [Fact]
    public void IdempotentOk_Replay_ReturnsTheStoredStatusAndByteExactBody()
    {
        const string storedBody = "{\"BookingId\":\"stored\",\"Status\":0}";
        var result = Result<IdempotentOutcome<string>>.Success(
            IdempotentOutcome<string>.Replay(StatusCodes.Status201Created, storedBody));

        IActionResult action = Controller().OkOutcome(result);

        var contentResult = Assert.IsType<ContentResult>(action);
        Assert.Equal(StatusCodes.Status201Created, contentResult.StatusCode);
        Assert.Equal(storedBody, contentResult.Content);
        Assert.Equal("application/json; charset=utf-8", contentResult.ContentType);
    }

    [Fact]
    public void IdempotentOutcome_Failure_MapsThroughTheSharedProblemDetailsMapper()
    {
        var result = Result<IdempotentOutcome<string>>.Failure(
            new Error("Bookings.ScheduleConflict", "Caregiver is already booked for this time slot."));

        IActionResult action = Controller().OkOutcome(result);

        var statusResult = Assert.IsType<ObjectResult>(action);
        var problemDetails = Assert.IsType<ProblemDetails>(statusResult.Value);
        Assert.Equal(StatusCodes.Status409Conflict, statusResult.StatusCode);
        Assert.Equal("Bookings.ScheduleConflict", problemDetails.Extensions["code"]);
    }

    [Theory]
    [InlineData("Bookings.IdempotencyKeyInvalid", 400)]
    [InlineData("Bookings.IdempotencyConflict", 409)]
    [InlineData("Bookings.AvailabilityConflict", 409)]
    [InlineData("Bookings.ScheduleConflict", 409)]
    [InlineData("Families.CareRecords.IdempotencyKeyInvalid", 400)]
    [InlineData("Families.CareRecords.IdempotencyConflict", 409)]
    [InlineData("CareHomes.Bookings.IdempotencyKeyInvalid", 400)]
    [InlineData("CareHomes.Bookings.IdempotencyConflict", 409)]
    public void TheEightNewCodes_MapToTheirDocumentedStatus(string code, int expectedStatus)
    {
        ProblemDetails problemDetails = ResultProblemDetailsMapper.Create(
            new Error(code, "test"),
            new DefaultHttpContext());

        Assert.Equal(expectedStatus, problemDetails.Status);
        Assert.Equal(code, problemDetails.Extensions["code"]);
    }

    private static void AssertCodedBadRequest(IActionResult? failure, string expectedCode)
    {
        Assert.NotNull(failure);

        var objectResult = Assert.IsAssignableFrom<ObjectResult>(failure);
        var problemDetails = Assert.IsType<ProblemDetails>(objectResult.Value);

        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
        Assert.Equal(StatusCodes.Status400BadRequest, problemDetails.Status);
        Assert.Equal(expectedCode, problemDetails.Extensions["code"]);
    }
}
