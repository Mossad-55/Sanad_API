using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.Abstractions;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.ValueObjects;
using Ahlena.Modules.Identity.Application.Abstractions.Data;
using Ahlena.Modules.Identity.Application.Abstractions.Security;
using Ahlena.Modules.Identity.Application.Authentication.Login;
using Ahlena.Modules.Identity.Application.Authentication.Tokens;
using Ahlena.Modules.Identity.Domain.Authentication.DeviceSessions;
using Ahlena.Modules.Identity.Domain.Authentication.VerificationRequests;
using Ahlena.Modules.Identity.Domain.Users;

namespace Ahlena.Modules.Identity.Application.Authentication.ElderlyLogin;

public sealed class VerifyElderlyLoginOtpCommandHandler :
    ICommandHandler<
        VerifyElderlyLoginOtpCommand,
        LoginResponse>
{
    private readonly IIdentityDbContext _dbContext;
    private readonly IOtpService _otpService;
    private readonly IAuthTokenService _tokenService;
    private readonly IDateTimeProvider _dateTimeProvider;

    public VerifyElderlyLoginOtpCommandHandler(
        IIdentityDbContext dbContext,
        IOtpService otpService,
        IAuthTokenService tokenService,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _otpService = otpService;
        _tokenService = tokenService;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<LoginResponse>> Handle(
        VerifyElderlyLoginOtpCommand request,
        CancellationToken cancellationToken)
    {
        PhoneNumber phoneNumber =
            PhoneNumber.Create(
                request.PhoneNumber);

        User? user =
            await _dbContext.Users
                .SingleOrDefaultAsync(
                    item =>
                        item.PhoneNumber ==
                        phoneNumber,
                    cancellationToken);

        if (user is null ||
            !IsEligibleElderlyUser(user))
        {
            return ElderlyLoginErrors
                .OtpVerificationFailed;
        }

        VerificationRequest? verificationRequest =
            await _dbContext.VerificationRequests
                .Where(item =>
                    item.UserId ==
                        user.Id &&
                    item.Target ==
                        phoneNumber.Value &&
                    item.Purpose ==
                        VerificationPurpose.ElderlyLogin &&
                    item.Channel ==
                        VerificationChannel.Sms &&
                    item.Status ==
                        VerificationStatus.Pending)
                .OrderByDescending(item =>
                    item.CreatedOnUtc)
                .FirstOrDefaultAsync(
                    cancellationToken);

        if (verificationRequest is null)
        {
            return ElderlyLoginErrors
                .OtpVerificationFailed;
        }

        DateTime utcNow =
            _dateTimeProvider.UtcNow;

        if (verificationRequest.IsExpired(
            utcNow))
        {
            verificationRequest.MarkExpired(
                utcNow);

            await _dbContext.SaveChangesAsync(
                cancellationToken);

            return ElderlyLoginErrors
                .OtpVerificationFailed;
        }

        bool codeIsValid =
            _otpService.Verify(
                request.Code,
                verificationRequest.OtpHash);

        if (!codeIsValid)
        {
            verificationRequest.RegisterFailedAttempt(
                utcNow);

            await _dbContext.SaveChangesAsync(
                cancellationToken);

            return ElderlyLoginErrors
                .OtpVerificationFailed;
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
            DeviceSessionPolicy.MaximumActiveSessions)
        {
            return ElderlyLoginErrors
                .SessionLimitReached;
        }

        GeneratedRefreshToken refreshToken =
            _tokenService.GenerateRefreshToken(
                utcNow);

        if (!user.PhoneVerified)
        {
            user.VerifyPhone(
                utcNow);
        }

        if (user.Status ==
            UserStatus.PendingVerification)
        {
            user.Activate(
                utcNow);
        }

        DeviceSession deviceSession =
            DeviceSession.Create(
                user.Id,
                request.DeviceName,
                request.DevicePlatform,
                request.AppVersion,
                refreshToken.Hash,
                utcNow,
                refreshToken.ExpiresOnUtc);

        // Session-bound Normal access issued after session creation.
        GeneratedAccessToken accessToken =
            _tokenService.GenerateAccessToken(
                user,
                utcNow,
                deviceSession.Id);

        verificationRequest.Verify(
            utcNow);

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

    private static bool IsEligibleElderlyUser(
        User user)
    {
        return (user.Status is
                    UserStatus.PendingVerification or
                    UserStatus.Active) &&
               user.Accounts.Count == 1 &&
               user.Accounts.Single().AccountType ==
                   AccountType.Elderly;
    }
}