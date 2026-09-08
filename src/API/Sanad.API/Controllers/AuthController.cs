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
using Sanad.BuildingBlocks.Application.Abstractions.Storage;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Identity.Application.Users;

namespace Sanad.API.Controllers;

[Route("api/v1/auth")]
public sealed class AuthController :
    ApiControllerBase
{
    private readonly ISender _sender;
    private readonly IFileStorage _fileStorage;

    public AuthController(
        ISender sender,
        IFileStorage fileStorage)
    {
        _sender = sender;
        _fileStorage = fileStorage;
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
                request.AccountType);

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

    [Authorize(
        Policy =
            AuthorizationPolicies.NormalAccess)]
    [HttpGet("avatar")]
    public async Task<IActionResult> GetAvatar(
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(
            out UserId userId))
        {
            return Unauthorized();
        }

        var result =
            await _sender.Send(
                new GetUserAvatarQuery(
                    userId),
                cancellationToken);

        if (result.IsFailure)
        {
            return ToActionResult(
                result);
        }

        return File(
            result.Value.Content,
            result.Value.ContentType,
            result.Value.FileName);
    }

    [Authorize(
        Policy =
            AuthorizationPolicies.NormalAccess)]
    [HttpPut("avatar")]
    [RequestSizeLimit(6_291_456)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(
        StatusCodes.Status204NoContent)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpsertAvatar(
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(
            out UserId userId))
        {
            return Unauthorized();
        }

        Result<StoredFile> upload =
            await SavePrivateAvatarImageAsync(
                file,
                cancellationToken);

        if (upload.IsFailure)
        {
            return ToActionResult(
                upload);
        }

        string imageKey = upload.Value.Key;

        var result =
            await _sender.Send(
                new UpsertUserAvatarCommand(
                    userId,
                    imageKey),
                cancellationToken);

        if (result.IsFailure)
        {
            await _fileStorage.DeleteAsync(
                imageKey,
                cancellationToken);

            return ToActionResult(
                result);
        }

        return ToActionResult(
            result);
    }

    [Authorize(
        Policy =
            AuthorizationPolicies.NormalAccess)]
    [HttpGet("identity-document")]
    [ProducesResponseType(
        typeof(IdentityDocumentResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetIdentityDocument(
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(
            out UserId userId))
        {
            return Unauthorized();
        }

        var result =
            await _sender.Send(
                new GetIdentityDocumentQuery(
                    userId),
                cancellationToken);

        return ToActionResult(
            result);
    }

    [Authorize(
        Policy =
            AuthorizationPolicies.NormalAccess)]
    [HttpPut("identity-document")]
    [RequestSizeLimit(10_485_760)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(
        typeof(IdentityDocumentResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpsertIdentityDocument(
        IFormFile? front,
        IFormFile? back,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(
            out UserId userId))
        {
            return Unauthorized();
        }

        Result<StoredFile> frontUpload =
            await SavePrivateIdentityImageAsync(
                front,
                cancellationToken);

        if (frontUpload.IsFailure)
        {
            return ToActionResult(
                frontUpload);
        }

        string frontKey = frontUpload.Value.Key;

        Result<StoredFile> backUpload =
            await SavePrivateIdentityImageAsync(
                back,
                cancellationToken);

        if (backUpload.IsFailure)
        {
            await _fileStorage.DeleteAsync(
                frontKey,
                cancellationToken);

            return ToActionResult(
                backUpload);
        }

        string backKey = backUpload.Value.Key;

        var result =
            await _sender.Send(
                new UpsertIdentityDocumentCommand(
                    userId,
                    frontKey,
                    backKey),
                cancellationToken);

        if (result.IsFailure)
        {
            await _fileStorage.DeleteAsync(
                frontKey,
                cancellationToken);

            await _fileStorage.DeleteAsync(
                backKey,
                cancellationToken);

            return ToActionResult(
                result);
        }

        return ToActionResult(
            result);
    }

    private async Task<Result<StoredFile>>
        SavePrivateAvatarImageAsync(
            IFormFile? file,
            CancellationToken cancellationToken)
    {
        if (file is null)
        {
            return Result<StoredFile>.Failure(
                StorageErrors.Empty);
        }

        string? contentType =
            NormalizeIdentityImageContentType(
                file.ContentType);

        if (contentType is null)
        {
            return Result<StoredFile>.Failure(
                StorageErrors.UnsupportedType);
        }

        await using Stream stream =
            file.OpenReadStream();

        return await _fileStorage.SavePrivateAsync(
            stream,
            contentType,
            file.Length,
            folder: AvatarStorage.Folder,
            cancellationToken);
    }

    private async Task<Result<StoredFile>>
        SavePrivateIdentityImageAsync(
            IFormFile? file,
            CancellationToken cancellationToken)
    {
        if (file is null)
        {
            return Result<StoredFile>.Failure(
                StorageErrors.Empty);
        }

        string? contentType =
            NormalizeIdentityImageContentType(
                file.ContentType);

        if (contentType is null)
        {
            return Result<StoredFile>.Failure(
                StorageErrors.UnsupportedType);
        }

        await using Stream stream =
            file.OpenReadStream();

        return await _fileStorage.SavePrivateAsync(
            stream,
            contentType,
            file.Length,
            folder: IdentityDocumentStorage.Folder,
            cancellationToken);
    }

    private static string? NormalizeIdentityImageContentType(
        string? contentType)
    {
        if (string.IsNullOrWhiteSpace(
            contentType))
        {
            return null;
        }

        string normalized =
            contentType.Trim();

        if (normalized.Equals(
            "image/jpg",
            StringComparison.OrdinalIgnoreCase))
        {
            return "image/jpeg";
        }

        if (normalized.Equals(
                "image/jpeg",
                StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals(
                "image/png",
                StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals(
                "image/webp",
                StringComparison.OrdinalIgnoreCase))
        {
            return normalized.ToLowerInvariant();
        }

        return null;
    }
}
