using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Controllers.Requests;
using Sanad.Modules.Identity.Application.Authentication.Registration;
using Sanad.Modules.Identity.Application.Authentication.Verification;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Application.Authentication.ElderlyLogin;
using Sanad.Modules.Identity.Application.Authentication.Login;
using Sanad.Modules.Identity.Application.Authentication.Refresh;
using Sanad.Modules.Identity.Application.Authentication.Password;
using Sanad.Modules.Identity.Application.Authentication.Sessions;
using Sanad.API.Authorization;

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

    [AllowAnonymous]
    [HttpPost("login")]
    [ProducesResponseType(
        typeof(LoginResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status403Forbidden)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var command =
            new LoginCommand(
                request.Email,
                request.Password,
                request.DeviceName,
                request.DevicePlatform,
                request.AppVersion);

        var result =
            await _sender.Send(
                command,
                cancellationToken);

        return ToActionResult(
            result);
    }
    [AllowAnonymous]
    [HttpPost("refresh")]
    [ProducesResponseType(
        typeof(RefreshTokenResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Refresh(
        [FromBody] RefreshTokenRequest request,
        CancellationToken cancellationToken)
    {
        var command =
            new RefreshTokenCommand(
                new DeviceSessionId(
                    request.DeviceSessionId),
                request.RefreshToken);

        var result =
            await _sender.Send(
                command,
            cancellationToken);

        return ToActionResult(
            result);
    }

    [AllowAnonymous]
    [HttpPost("elderly/request-otp")]
    [ProducesResponseType(
    StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RequestElderlyOtp(
    [FromBody] RequestElderlyLoginOtpRequest request,
    CancellationToken cancellationToken)
    {
        var command =
            new RequestElderlyLoginOtpCommand(
                request.PhoneNumber);

        var result =
            await _sender.Send(
                command,
                cancellationToken);

        return ToActionResult(
            result);
    }

    [AllowAnonymous]
    [HttpPost("elderly/verify-otp")]
    [ProducesResponseType(
        typeof(LoginResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status409Conflict)]
    public async Task<IActionResult> VerifyElderlyOtp(
        [FromBody] VerifyElderlyLoginOtpRequest request,
        CancellationToken cancellationToken)
    {
        var command =
            new VerifyElderlyLoginOtpCommand(
                request.PhoneNumber,
                request.Code,
                request.DeviceName,
                request.DevicePlatform,
                request.AppVersion);

        var result =
            await _sender.Send(
                command,
                cancellationToken);

        return ToActionResult(
            result);
    }

    [AllowAnonymous]
    [HttpPost("password/reset/request")]
    [ProducesResponseType(
        StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RequestPasswordReset(
        [FromBody] RequestPasswordResetRequest request,
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new RequestPasswordResetCommand(
                    request.Email),
                cancellationToken);

        return ToActionResult(
            result);
    }

    [AllowAnonymous]
    [HttpPost("password/reset")]
    [ProducesResponseType(
        StatusCodes.Status204NoContent)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ResetPassword(
        [FromBody] ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new ResetPasswordCommand(
                    request.Email,
                    request.OtpCode,
                    request.NewPassword),
                cancellationToken);

        return ToActionResult(
            result);
    }

    [Authorize(
        Policy =
            AuthorizationPolicies.NormalAccess)]
    [HttpPost("password/change")]
    [ProducesResponseType(
        StatusCodes.Status204NoContent)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ChangePassword(
        [FromBody] ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(
            out UserId userId))
        {
            return Unauthorized();
        }

        var result =
            await _sender.Send(
                new ChangePasswordCommand(
                    userId,
                    request.CurrentPassword,
                    request.NewPassword),
                cancellationToken);

        return ToActionResult(
            result);
    }

    [Authorize(
        Policy =
            AuthorizationPolicies.NormalAccess)]
    [HttpPost("sessions/logout")]
    [ProducesResponseType(
    StatusCodes.Status204NoContent)]
    [ProducesResponseType(
    typeof(ProblemDetails),
    StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> LogoutCurrentSession(
    CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(
                out UserId userId))
        {
            return Unauthorized();
        }

        if (!TryGetCurrentDeviceSessionId(
                out DeviceSessionId deviceSessionId))
        {
            return BadRequestWithCode(
                "Api.Auth.InvalidDeviceSessionHeader");
        }

        var result =
            await _sender.Send(
                new LogoutCurrentSessionCommand(
                    deviceSessionId,
                    userId),
                cancellationToken);

        return ToActionResult(
            result);
    }

    [Authorize(
        Policy =
            AuthorizationPolicies.NormalAccess)]
    [HttpPost("sessions/logout-all")]
    [ProducesResponseType(
        StatusCodes.Status204NoContent)]
    public async Task<IActionResult> LogoutAllSessions(
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(
                out UserId userId))
        {
            return Unauthorized();
        }

        var result =
            await _sender.Send(
                new LogoutAllSessionsCommand(
                    userId),
                cancellationToken);

        return ToActionResult(
            result);
    }

    [Authorize(
        Policy =
            AuthorizationPolicies.NormalAccess)]
    [HttpGet("sessions")]
    [ProducesResponseType(
        typeof(ActiveSessionsResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetActiveSessions(
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(
                out UserId userId))
        {
            return Unauthorized();
        }

        var result =
            await _sender.Send(
                new GetActiveSessionsQuery(
                    userId),
                cancellationToken);

        return ToActionResult(
            result);
    }

    [Authorize(
        Policy =
            AuthorizationPolicies.NormalAccess)]
    [HttpDelete("sessions/{sessionId:guid}")]
    [ProducesResponseType(
        StatusCodes.Status204NoContent)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeSession(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(
                out UserId userId))
        {
            return Unauthorized();
        }

        var result =
            await _sender.Send(
                new RevokeSessionCommand(
                    new DeviceSessionId(
                        sessionId),
                    userId),
                cancellationToken);

        return ToActionResult(
            result);
    }
}