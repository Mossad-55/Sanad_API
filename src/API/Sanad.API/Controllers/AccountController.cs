using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.API.Controllers.Requests;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Application.Users;

namespace Sanad.API.Controllers;

[Route("api/v1/account")]
public sealed class AccountController :
    ApiControllerBase
{
    private readonly ISender _sender;

    public AccountController(
        ISender sender)
    {
        _sender = sender;
    }

    [Authorize(
        Policy =
            AuthorizationPolicies.NormalAccess)]
    [HttpGet]
    [ProducesResponseType(
        typeof(MyProfileResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMyAccount(
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(
            out UserId userId))
        {
            return Unauthorized();
        }

        var result =
            await _sender.Send(
                new GetMyProfileQuery(
                    userId),
                cancellationToken);

        return ToActionResult(
            result);
    }

    [Authorize(
        Policy =
            AuthorizationPolicies.NormalAccess)]
    [HttpPut]
    [ProducesResponseType(
        typeof(UpdateMyProfileResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateMyAccount(
        [FromBody] UpdateMyAccountRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(
            out UserId userId))
        {
            return Unauthorized();
        }

        var result =
            await _sender.Send(
                new UpdateMyProfileCommand(
                    userId,
                    request.ArabicFullName,
                    request.EnglishFullName,
                    request.Email,
                    request.PhoneNumber),
                cancellationToken);

        return ToActionResult(
            result);
    }
}
