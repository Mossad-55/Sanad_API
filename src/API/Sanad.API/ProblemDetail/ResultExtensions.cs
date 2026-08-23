using Sanad.BuildingBlocks.Application.Results;

namespace Sanad.API.ProblemDetail;

public static class ResultExtensions
{
    public static IResult ToHttpResult(
        this Result result,
        HttpContext httpContext)
    {
        if (result.IsSuccess)
        {
            return Results.NoContent();
        }

        return Results.Problem(
            ResultProblemDetailsMapper.Create(
                result.Error,
                httpContext));
    }

    public static IResult ToHttpResult<T>(
        this Result<T> result,
        HttpContext httpContext)
    {
        if (result.IsSuccess)
        {
            return Results.Ok(
                result.Value);
        }

        return Results.Problem(
            ResultProblemDetailsMapper.Create(
                result.Error,
                httpContext));
    }
}