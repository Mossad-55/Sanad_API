using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ahlena.API.Controllers.Requests;
using Ahlena.Modules.Identity.Application.Authentication.Registration;
using Ahlena.Modules.Identity.Application.Authentication.Verification;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Identity.Application.Authentication.ElderlyLogin;
using Ahlena.Modules.Identity.Application.Authentication.Login;
using Ahlena.Modules.Identity.Application.Authentication.Refresh;
using Ahlena.Modules.Identity.Application.Authentication.Password;
using Ahlena.Modules.Identity.Application.Authentication.Sessions;
using Ahlena.API.Authorization;
using Ahlena.BuildingBlocks.Application.Abstractions.Storage;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.Modules.Identity.Application.Users;

namespace Ahlena.API.Controllers;

[Route("api/v1/auth")]
public sealed class AuthController :
    ApiControllerBase
{
    private readonly ISender _sender;
    private readonly IFileStorage _fileStorage;
    private readonly Realtime.ChatConnectionEvictor? _sessionEvictor;

    public AuthController(
        ISender sender,
        IFileStorage fileStorage,
        Realtime.ChatConnectionEvictor? sessionEvictor = null)
    {
        _sender = sender;
        _fileStorage = fileStorage;
        _sessionEvictor = sessionEvictor;
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
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status502BadGateway)]
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
                request.LoginIdentifier,
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
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status404NotFound)]
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
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status400BadRequest)]
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
        StatusCodes.Status400BadRequest)]
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
        StatusCodes.Status400BadRequest)]
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
    [ProducesResponseType(
        StatusCodes.Status401Unauthorized)]
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

        // Session revocation takes effect through the shared validator on
        // the next hub call/dispatch; evict this device's joins/hints now
        // so no new realtime dispatch observes stale eligibility.
        if (result.IsSuccess && _sessionEvictor is not null)
        {
            try
            {
                await _sessionEvictor.EvictSessionAsync(
                    userId, deviceSessionId, cancellationToken);
            }
            catch (Exception)
            {
            }
        }

        return ToActionResult(
            result);
    }

    [Authorize(
        Policy =
            AuthorizationPolicies.NormalAccess)]
    [HttpPost("sessions/logout-all")]
    [ProducesResponseType(
        StatusCodes.Status204NoContent)]
    [ProducesResponseType(
        StatusCodes.Status401Unauthorized)]
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

        if (result.IsSuccess && _sessionEvictor is not null)
        {
            try
            {
                await _sessionEvictor.EvictUserSessionsAsync(
                    userId, cancellationToken);
            }
            catch (Exception)
            {
            }
        }

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
    [ProducesResponseType(
        StatusCodes.Status401Unauthorized)]
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
    [ProducesResponseType(
        StatusCodes.Status401Unauthorized)]
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

        if (result.IsSuccess && _sessionEvictor is not null)
        {
            try
            {
                await _sessionEvictor.EvictSessionAsync(
                    userId,
                    new DeviceSessionId(sessionId),
                    cancellationToken);
            }
            catch (Exception)
            {
            }
        }

        return ToActionResult(
            result);
    }

    /// <summary>
    /// Streams the caller's stored avatar image. The content type is always
    /// one of the accepted upload types (jpeg, png, webp); a missing avatar is
    /// <c>404 Identity.Avatar.NotFound</c>.
    /// </summary>
    [Authorize(
        Policy =
            AuthorizationPolicies.NormalAccess)]
    [HttpGet("avatar")]
    [Produces("image/jpeg", "image/png", "image/webp")]
    [ProducesResponseType(
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status404NotFound)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status409Conflict)]
    [ProducesResponseType(
        StatusCodes.Status401Unauthorized)]
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
    [ProducesResponseType(
        StatusCodes.Status401Unauthorized)]
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
    [ProducesResponseType(
        StatusCodes.Status401Unauthorized)]
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
    [ProducesResponseType(
        StatusCodes.Status401Unauthorized)]
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
            CancellationToken cancellationToken) =>
        await SaveVerifiedPrivateImageAsync(
            file,
            AvatarStorage.Folder,
            cancellationToken);

    private async Task<Result<StoredFile>>
        SavePrivateIdentityImageAsync(
            IFormFile? file,
            CancellationToken cancellationToken) =>
        await SaveVerifiedPrivateImageAsync(
            file,
            IdentityDocumentStorage.Folder,
            cancellationToken);

    // 5 MB, the documented per-image limit and the private-storage ceiling.
    private const long MaximumPrivateImageBytes =
        5_242_880;

    /// <summary>
    /// Stores an avatar/identity image in private storage after verifying that
    /// the declared MIME type is an accepted image (normalized so
    /// <c>image/jpg</c> is stored as <c>image/jpeg</c>) AND that the actual
    /// bytes carry the matching signature. The declared type alone is never
    /// trusted, so a mislabelled payload (for example a PDF announced as a
    /// JPEG) is refused with no storage write.
    /// </summary>
    private async Task<Result<StoredFile>>
        SaveVerifiedPrivateImageAsync(
            IFormFile? file,
            string folder,
            CancellationToken cancellationToken)
    {
        if (file is null || file.Length <= 0)
        {
            return Result<StoredFile>.Failure(
                StorageErrors.Empty);
        }

        if (file.Length > MaximumPrivateImageBytes)
        {
            return Result<StoredFile>.Failure(
                StorageErrors.TooLarge);
        }

        string? contentType =
            NormalizeIdentityImageContentType(
                file.ContentType);

        if (contentType is null)
        {
            return Result<StoredFile>.Failure(
                StorageErrors.UnsupportedType);
        }

        await using var content =
            new MemoryStream();

        await file.CopyToAsync(
            content,
            cancellationToken);

        if (!HasMatchingImageSignature(
                contentType,
                content.GetBuffer().AsSpan(0, (int)content.Length)))
        {
            return Result<StoredFile>.Failure(
                StorageErrors.UnsupportedType);
        }

        content.Position = 0;

        return await _fileStorage.SavePrivateAsync(
            content,
            contentType,
            file.Length,
            folder,
            cancellationToken);
    }

    /// <summary>
    /// Byte-signature check for the accepted image types; any other declared
    /// type has no allowed signature and is rejected.
    /// </summary>
    private static bool HasMatchingImageSignature(
        string contentType,
        ReadOnlySpan<byte> bytes) =>
        contentType switch
        {
            "image/jpeg" => bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF,
            "image/png" => bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
            "image/webp" => bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8),
            _ => false
        };

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
