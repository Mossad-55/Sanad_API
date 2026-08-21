using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Identity.Application.Abstractions.Data;
using Sanad.Modules.Identity.Application.Abstractions.Security;
using Sanad.Modules.Identity.Application.Authentication.Tokens;
using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.Modules.Identity.Application.Authentication.Refresh;

public sealed class RefreshTokenCommandHandler :
    ICommandHandler<
        RefreshTokenCommand,
        RefreshTokenResponse>
{
    private readonly IIdentityDbContext _dbContext;
    private readonly IAuthTokenService _tokenService;
    private readonly IDateTimeProvider _dateTimeProvider;

    public RefreshTokenCommandHandler(
        IIdentityDbContext dbContext,
        IAuthTokenService tokenService,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _tokenService = tokenService;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<RefreshTokenResponse>> Handle(
        RefreshTokenCommand request,
        CancellationToken cancellationToken)
    {
        DeviceSession? session =
            await _dbContext.DeviceSessions
                .SingleOrDefaultAsync(
                    item =>
                        item.Id ==
                        request.DeviceSessionId,
                    cancellationToken);

        if (session is null)
        {
            return RefreshTokenErrors
                .SessionNotFound;
        }

        DateTime utcNow =
            _dateTimeProvider.UtcNow;

        if (session.IsRevoked)
        {
            return RefreshTokenErrors
                .SessionRevoked;
        }

        if (session.IsExpired(
            utcNow))
        {
            return RefreshTokenErrors
                .SessionExpired;
        }

        User? user =
            await _dbContext.Users
                .SingleOrDefaultAsync(
                    item =>
                        item.Id ==
                        session.UserId,
                    cancellationToken);

        if (user is null)
        {
            return RefreshTokenErrors
                .UserNotFound;
        }

        if (user.Status !=
            UserStatus.Active)
        {
            session.Revoke(
                "User is not Active.",
                utcNow);

            await _dbContext.SaveChangesAsync(
                cancellationToken);

            return RefreshTokenErrors
                .UserNotActive;
        }

        bool refreshTokenIsValid =
            _tokenService.VerifyRefreshToken(
                request.RefreshToken,
                session.RefreshTokenHash);

        if (!refreshTokenIsValid)
        {
            session.RegisterRefreshTokenReuse(
                utcNow);

            DeviceSession[] otherSessions =
                await _dbContext.DeviceSessions
                    .Where(item =>
                        item.UserId ==
                            session.UserId &&
                        item.Id !=
                            session.Id &&
                        item.RevokedOnUtc ==
                            null)
                    .ToArrayAsync(
                        cancellationToken);

            foreach (
                DeviceSession otherSession
                in otherSessions)
            {
                otherSession.Revoke(
                    "Refresh token reuse detected " +
                    "on another session.",
                    utcNow);
            }

            await _dbContext.SaveChangesAsync(
                cancellationToken);

            return RefreshTokenErrors
                .ReuseDetected;
        }

        GeneratedAccessToken accessToken =
            _tokenService.GenerateAccessToken(
                user,
                utcNow);

        GeneratedRefreshToken refreshToken =
            _tokenService.GenerateRefreshToken(
                utcNow);

        session.RotateRefreshToken(
            refreshToken.Hash,
            refreshToken.ExpiresOnUtc,
            utcNow);

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        return new RefreshTokenResponse(
            session.Id,
            accessToken.PlainTextToken,
            accessToken.ExpiresOnUtc,
            refreshToken.PlainTextToken,
            refreshToken.ExpiresOnUtc);
    }
}