using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Application.Abstractions.Data;
using Sanad.Modules.Identity.Application.Abstractions.Security;
using Sanad.Modules.Identity.Application.Authentication.Tokens;
using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.Modules.Identity.Application.Authentication.Login;

public sealed class LoginCommandHandler :
    ICommandHandler<
        LoginCommand,
        LoginResponse>
{
    private readonly IIdentityDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IAuthTokenService _tokenService;
    private readonly IDateTimeProvider _dateTimeProvider;

    public LoginCommandHandler(
        IIdentityDbContext dbContext,
        IPasswordHasher passwordHasher,
        IAuthTokenService tokenService,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<LoginResponse>> Handle(
        LoginCommand request,
        CancellationToken cancellationToken)
    {
        Email email =
            Email.Create(
                request.Email);

        User? user =
            await _dbContext.Users
                .SingleOrDefaultAsync(
                    item =>
                        item.Email ==
                        email,
                    cancellationToken);

        if (user is null ||
            user.Password is null)
        {
            return LoginErrors
                .InvalidCredentials;
        }

        PasswordVerificationResult
            passwordVerification =
                _passwordHasher.Verify(
                    user.Password.PasswordHash,
                    request.Password);

        if (passwordVerification ==
            PasswordVerificationResult.Failed)
        {
            return LoginErrors
                .InvalidCredentials;
        }

        if (user.Status ==
            UserStatus.Suspended)
        {
            return LoginErrors
                .UserSuspended;
        }

        if (user.Status ==
            UserStatus.Blocked)
        {
            return LoginErrors
                .UserBlocked;
        }

        DateTime utcNow =
            _dateTimeProvider.UtcNow;

        if (passwordVerification ==
            PasswordVerificationResult
                .SuccessRehashNeeded)
        {
            string updatedPasswordHash =
                _passwordHasher.Hash(
                    request.Password);

            user.RehashPasswordHash(
                updatedPasswordHash,
                utcNow);
        }

        if (user.Status ==
            UserStatus.PendingVerification)
        {
            GeneratedAccessToken restrictedToken =
                _tokenService
                    .GenerateRestrictedVerificationToken(
                        user,
                        utcNow);

            user.UpdateLastLogin(
                utcNow);

            await _dbContext.SaveChangesAsync(
                cancellationToken);

            return new LoginResponse(
                user.Id,
                AuthAccessType
                    .RestrictedVerification,
                restrictedToken.PlainTextToken,
                restrictedToken.ExpiresOnUtc,
                RefreshToken: null,
                RefreshTokenExpiresOnUtc: null,
                DeviceSessionId: null,
                user.EmailVerified,
                user.PhoneVerified);
        }

        if (user.Status !=
            UserStatus.Active)
        {
            return LoginErrors
                .InvalidCredentials;
        }

        int activeSessionCount =
            await _dbContext.DeviceSessions
                .CountAsync(
                    session =>
                        session.UserId ==
                            user.Id &&
                        session.RevokedOnUtc ==
                            null &&
                        session.ExpiresOnUtc >
                            utcNow,
                    cancellationToken);

        if (activeSessionCount >=
            DeviceSessionPolicy
                .MaximumActiveSessions)
        {
            return LoginErrors
                .SessionLimitReached;
        }

        GeneratedAccessToken accessToken =
            _tokenService.GenerateAccessToken(
                user,
                utcNow);

        GeneratedRefreshToken refreshToken =
            _tokenService.GenerateRefreshToken(
                utcNow);

        DeviceSession deviceSession =
            DeviceSession.Create(
                user.Id,
                request.DeviceName,
                request.DevicePlatform,
                request.AppVersion,
                refreshToken.Hash,
                utcNow,
                refreshToken.ExpiresOnUtc);

        _dbContext.DeviceSessions.Add(
            deviceSession);

        user.UpdateLastLogin(
            utcNow);

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        return new LoginResponse(
            user.Id,
            AuthAccessType.Normal,
            accessToken.PlainTextToken,
            accessToken.ExpiresOnUtc,
            refreshToken.PlainTextToken,
            refreshToken.ExpiresOnUtc,
            deviceSession.Id,
            user.EmailVerified,
            user.PhoneVerified);
    }
}