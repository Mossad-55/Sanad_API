using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Sanad.API.ProblemDetail;

public sealed class ValidationExceptionHandler :
    IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not ValidationException validationException)
        {
            return false;
        }

        Dictionary<string, string[]> errors =
            validationException.Errors
                .GroupBy(
                    failure =>
                        string.IsNullOrWhiteSpace(failure.PropertyName)
                            ? "request"
                            : failure.PropertyName)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .Select(failure => failure.ErrorMessage)
                        .Distinct()
                        .ToArray());

        var problemDetails = new ProblemDetails
        {
            Type = "https://httpstatuses.com/400",
            Title = "Validation Failed",
            Status = StatusCodes.Status400BadRequest,
            Detail = "One or more validation errors occurred.",
            Instance = httpContext.Request.Path
        };

        problemDetails.Extensions["code"] =
            "Api.Validation.Failed";

        problemDetails.Extensions["traceId"] =
            httpContext.TraceIdentifier;

        problemDetails.Extensions["errors"] =
            errors;

        httpContext.Response.StatusCode =
            StatusCodes.Status400BadRequest;

        httpContext.Response.ContentType =
            "application/problem+json";

        await JsonSerializer.SerializeAsync(
            httpContext.Response.Body,
            problemDetails,
            new JsonSerializerOptions(
                JsonSerializerDefaults.Web),
            cancellationToken);

        return true;
    }
}
