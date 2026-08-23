using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Sanad.API.ProblemDetail;
using System.Text.Json;

namespace Sanad.UnitTests.API;

public sealed class ValidationExceptionHandlerTests
{
    [Fact]
    public async Task TryHandleAsync_ShouldReturnBadRequestProblemDetails_ForValidationException()
    {
        var httpContext = new DefaultHttpContext();

        httpContext.TraceIdentifier = "trace-id";
        httpContext.Request.Path = "/api/v1/auth/register";
        httpContext.Response.Body = new MemoryStream();

        var exception = new ValidationException(
        [
            new ValidationFailure("Email", "Email is invalid."),
            new ValidationFailure("Email", "Email is required."),
            new ValidationFailure("Password", "Password is too short.")
        ]);

        var handler = new ValidationExceptionHandler();

        bool handled = await handler.TryHandleAsync(
            httpContext,
            exception,
            CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status400BadRequest, httpContext.Response.StatusCode);

        httpContext.Response.Body.Position = 0;

        using JsonDocument document = await JsonDocument.ParseAsync(
            httpContext.Response.Body);

        JsonElement root = document.RootElement;

        Assert.Equal(400, root.GetProperty("status").GetInt32());
        Assert.Equal("Validation Failed", root.GetProperty("title").GetString());
        Assert.Equal("Api.Validation.Failed", root.GetProperty("code").GetString());
        Assert.Equal("trace-id", root.GetProperty("traceId").GetString());

        JsonElement errors = root.GetProperty("errors");

        Assert.Equal(2, errors.GetProperty("Email").GetArrayLength());
        Assert.Equal(1, errors.GetProperty("Password").GetArrayLength());
    }

    [Fact]
    public async Task TryHandleAsync_ShouldReturnFalse_ForOtherException()
    {
        var httpContext = new DefaultHttpContext();
        var handler = new ValidationExceptionHandler();

        bool handled = await handler.TryHandleAsync(
            httpContext,
            new InvalidOperationException(),
            CancellationToken.None);

        Assert.False(handled);
    }
}
