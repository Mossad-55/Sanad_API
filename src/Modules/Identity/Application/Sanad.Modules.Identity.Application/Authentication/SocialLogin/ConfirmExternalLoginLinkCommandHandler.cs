using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Application.Abstractions.Data;
using Sanad.Modules.Identity.Application.Abstractions.Security;
using Sanad.Modules.Identity.Application.Authentication.Tokens;
using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;
using Sanad.Modules.Identity.Domain.Authentication.VerificationRequests;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.Modules.Identity.Application.Authentication.SocialLogin;

public sealed class ConfirmExternalLoginLinkCommandHandler :
    ICommandHandler<
        ConfirmExternalLoginLinkCommand,
        StartSocialLoginResponse>
{
    private readonly IIdentityDbContext _dbContext;
    private readonly ISocialAuthenticationChallengeStore _challengeStore;
    private readonly IOtpService _otpService;
    private readonly IAuthTokenService _tokenService;
    private readonly IDateTimeProvider _dateTimeProvider;

    public ConfirmExternalLoginLinkCommandHandler(
        IIdentityDbContext dbContext,
        ISocialAuthenticationChallengeStore challengeStore,
        IOtpService otpService,
        IAuthTokenService tokenService,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _challengeStore = challengeStore;
        _otpService = otpService;
        _tokenService = tokenService;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<StartSocialLoginResponse>> Handle(
        ConfirmExternalLoginLinkCommand request,
        CancellationToken cancellationToken)
    {
        DateTime utcNow = _dateTimeProvider.UtcNow;

        SocialAuthenticationChallenge? challenge =
            await _challengeStore.ConsumeAsync(
                request.OpaqueChallenge,
                utcNow,
                cancellationToken);

        if (!TryGetExistingUserChallenge(
            challenge,
            out UserId userId,
            out VerificationRequestId verificationRequestId,
            out Email verifiedEmail))
        {
            return SocialLoginErrors.ExternalLinkConfirmationFailed;
        }

        User? user =
            await _dbContext.Users
                .SingleOrDefaultAsync(
                    item => item.Id == userId,
                    cancellationToken);

        VerificationRequest? verificationRequest =
            await _dbContext.VerificationRequests
                .SingleOrDefaultAsync(
                    item => item.Id == verificationRequestId,
                    cancellationToken);

        if (user is null ||
            verificationRequest is null ||
            !IsEligibleNonElderlyUser(user) ||
            user.Status is not (
                UserStatus.PendingVerification or
                UserStatus.Active) ||
            !IsMatchingVerificationRequest(
                verificationRequest,
                user.Id,
                verifiedEmail))
        {
            return SocialLoginErrors.ExternalLinkConfirmationFailed;
        }

        bool providerSubjectAlreadyLinked =
            await _dbContext.Users
                .AnyAsync(
                    item =>
                        item.ExternalLogins.Any(
                            externalLogin =>
                                externalLogin.Provider ==
                                    challenge!.Provider &&
                                externalLogin.ProviderSubject ==
                                    challenge.ProviderSubject),
                    cancellationToken);

        if (providerSubjectAlreadyLinked ||
            user.ExternalLogins.Any(
                externalLogin =>
                    externalLogin.Provider ==
                    challenge!.Provider))
        {
            return SocialLoginErrors.ExternalLinkConfirmationFailed;
        }

        if (verificationRequest.IsExpired(utcNow))
        {
            verificationRequest.MarkExpired(utcNow);

            await _dbContext.SaveChangesAsync(cancellationToken);

            return SocialLoginErrors.ExternalLinkConfirmationFailed;
        }

        if (!_otpService.Verify(request.Code, verificationRequest.OtpHash))
        {
            verificationRequest.RegisterFailedAttempt(utcNow);

            await _dbContext.SaveChangesAsync(cancellationToken);

            return SocialLoginErrors.ExternalLinkConfirmationFailed;
        }

        bool normalAccess =
            user.Status == UserStatus.Active ||
            user.PhoneVerified;

        if (normalAccess)
        {
            int activeSessionCount =
                await _dbContext.DeviceSessions.CountAsync(
                    session =>
                        session.UserId == user.Id &&
                        session.RevokedOnUtc == null &&
                        session.ExpiresOnUtc > utcNow,
                    cancellationToken);

            if (activeSessionCount >= DeviceSessionPolicy.MaximumActiveSessions)
            {
                return SocialLoginErrors.SessionLimitReached;
            }
        }

        GeneratedAccessToken accessToken =
            normalAccess
                ? _tokenService.GenerateAccessToken(user, utcNow)
                : _tokenService.GenerateRestrictedVerificationToken(user, utcNow);

        GeneratedRefreshToken? refreshToken =
            normalAccess
                ? _tokenService.GenerateRefreshToken(utcNow)
                : null;

        user.LinkExternalLogin(
            challenge!.Provider,
            challenge.ProviderSubject,
            utcNow);

        user.VerifyEmail(utcNow);
        verificationRequest.Verify(utcNow);

        if (user.Status == UserStatus.PendingVerification && user.PhoneVerified)
        {
            user.Activate(utcNow);
        }

        DeviceSession? deviceSession = null;

        if (normalAccess)
        {
            deviceSession = DeviceSession.Create(
                user.Id,
                request.DeviceName,
                request.DevicePlatform,
                request.AppVersion,
                refreshToken!.Hash,
                utcNow,
                refreshToken.ExpiresOnUtc);

            _dbContext.DeviceSessions.Add(deviceSession);
        }

        user.UpdateLastLogin(utcNow);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new StartSocialLoginResponse(
            normalAccess
                ? AuthAccessType.Normal
                : AuthAccessType.RestrictedVerification,
            accessToken.PlainTextToken,
            accessToken.ExpiresOnUtc,
            refreshToken?.PlainTextToken,
            refreshToken?.ExpiresOnUtc,
            deviceSession?.Id,
            OpaqueChallenge: null);
    }

    private static bool IsEligibleNonElderlyUser(User user)
    {
        return user.Accounts.Any(
            account => account.AccountType != AccountType.Elderly);
    }

    private static bool IsMatchingVerificationRequest(
        VerificationRequest request,
        UserId userId,
        Email verifiedEmail)
    {
        return request.UserId == userId &&
               request.Target == verifiedEmail.Value &&
               request.Channel == VerificationChannel.Email &&
               request.Purpose == VerificationPurpose.ConfirmExternalLoginLink &&
               request.Status == VerificationStatus.Pending;
    }

    private static bool TryGetExistingUserChallenge(
        SocialAuthenticationChallenge? challenge,
        out UserId userId,
        out VerificationRequestId verificationRequestId,
        out Email verifiedEmail)
    {
        userId = default;
        verificationRequestId = default;
        verifiedEmail = default!;

        if (challenge is null ||
            !challenge.ExistingUserId.HasValue ||
            !challenge.LinkVerificationRequestId.HasValue ||
            string.IsNullOrWhiteSpace(challenge.VerifiedEmail))
        {
            return false;
        }

        try
        {
            verifiedEmail = Email.Create(challenge.VerifiedEmail);
        }
        catch (DomainException)
        {
            return false;
        }

        userId = challenge.ExistingUserId.Value;
        verificationRequestId = challenge.LinkVerificationRequestId.Value;

        return true;
    }
}
