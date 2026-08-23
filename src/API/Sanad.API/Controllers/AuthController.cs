using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Controllers.Requests;
using Sanad.Modules.Identity.Application.Authentication.Registration;
using Sanad.Modules.Identity.Application.Authentication.Verification;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.API.Controllers;

[Route("api/v1/auth")]
public sealed class AuthController :
    ApiControllerBase
{
    private readonly ISender _sender;

    public AuthController(
        ISender sender)
    {
        _sender = sender;
    }

    [AllowAnonymous]
    [HttpPost("register")]
    [ProducesResponseType(
        typeof(RegisterUserResponse),
        StatusCodes.Status201Created)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register(
        [FromBody] RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var command =
            new RegisterUserCommand(
                request.ArabicFullName,
                request.EnglishFullName,
                request.Email,
                request.PhoneNumber,
                request.Password,
                request.AccountType,
                request.AvatarUrl);

        var result =
            await _sender.Send(
                command,
                cancellationToken);

        if (result.IsFailure)
        {
            return ToActionResult(
                result);
        }

        return StatusCode(
            StatusCodes.Status201Created,
            result.Value);
    }

    [AllowAnonymous]
    [HttpPost("verification/verify")]
    [ProducesResponseType(
        typeof(VerifyOtpResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> VerifyOtp(
        [FromBody] VerifyOtpRequest request,
        CancellationToken cancellationToken)
    {
        var command =
            new VerifyOtpCommand(
                new VerificationRequestId(
                    request.VerificationRequestId),
                request.Code);

        var result =
            await _sender.Send(
                command,
                cancellationToken);

        return ToActionResult(
            result);
    }

    [AllowAnonymous]
    [HttpPost("verification/resend")]
    [ProducesResponseType(
        typeof(ResendOtpResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status404NotFound)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ResendOtp(
        [FromBody] ResendOtpRequest request,
        CancellationToken cancellationToken)
    {
        var command =
            new ResendOtpCommand(
                new VerificationRequestId(
                    request.VerificationRequestId));

        var result =
            await _sender.Send(
                command,
                cancellationToken);

        return ToActionResult(
            result);
    }
}