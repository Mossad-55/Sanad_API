using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Application.Abstractions.Data;
using Sanad.Modules.Identity.Application.Abstractions.Messaging;
using Sanad.Modules.Identity.Application.Abstractions.Security;
using Sanad.Modules.Identity.Application.Authentication.Tokens;
using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;
using Sanad.Modules.Identity.Domain.Authentication.ExternalLogins;
using Sanad.Modules.Identity.Domain.Authentication.VerificationRequests;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.Modules.Identity.Application.Authentication.SocialLogin;

public sealed class StartSocialLoginCommandHandler :
    ICommandHandler<
        StartSocialLoginCommand,
        StartSocialLoginResponse>
{
    private readonly IIdentityDbContext _dbContext;
    private readonly IExternalIdentityVerifier _externalIdentityVerifier;
    private readonly ISocialAuthenticationChallengeStore _challengeStore;
    private readonly IOtpService _otpService;
    private readonly IEmailSender _emailSender;
    private readonly IAuthTokenService _tokenService;
    private readonly IDateTimeProvider _dateTimeProvider;

    public StartSocialLoginCommandHandler(
        IIdentityDbContext dbContext,
        IExternalIdentityVerifier externalIdentityVerifier,
        ISocialAuthenticationChallengeStore challengeStore,
        IOtpService otpService,
        IEmailSender emailSender,
        IAuthTokenService tokenService,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _externalIdentityVerifier = externalIdentityVerifier;
        _challengeStore = challengeStore;
        _otpService = otpService;
        _emailSender = emailSender;
        _tokenService = tokenService;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<StartSocialLoginResponse>> Handle(
        StartSocialLoginCommand request,
        CancellationToken cancellationToken)
    {
        VerifiedExternalIdentity? externalIdentity =
            await _externalIdentityVerifier.VerifyAsync(
                request.Provider,
                request.ProviderCredential,
                cancellationToken);

        if (!TryNormalizeExternalIdentity(
            request.Provider,
            externalIdentity,
            out string providerSubject,
            out Email? verifiedEmail))
        {
            return SocialLoginErrors.AuthenticationFailed;
        }

        User? linkedUser =
            await _dbContext.Users
                .SingleOrDefaultAsync(
                    user =>
                        user.ExternalLogins.Any(
                            externalLogin =>
                                externalLogin.Provider ==
                                    request.Provider &&
                                externalLogin.ProviderSubject ==
                                    providerSubject),
                    cancellationToken);

        DateTime utcNow =
            _dateTimeProvider.UtcNow;

        if (linkedUser is not null)
        {
            return await HandleLinkedUserAsync(
                linkedUser,
                request,
                utcNow,
                cancellationToken);
        }

        if (verifiedEmail is not null)
        {
            User? matchingEmailUser =
                await _dbContext.Users
                    .SingleOrDefaultAsync(
                        user =>
                            user.Email ==
                            verifiedEmail,
                        cancellationToken);

            if (matchingEmailUser is not null)
            {
                if (!IsEligibleSocialUser(
                    matchingEmailUser))
                {
                    return SocialLoginErrors.AuthenticationFailed;
                }

                return await CreateExistingEmailChallengeAsync(
                    matchingEmailUser,
                    request.Provider,
                    providerSubject,
                    verifiedEmail,
                    utcNow,
                    cancellationToken);
            }
        }

        return await CreateNewSocialUserChallengeAsync(
            request.Provider,
            providerSubject,
            verifiedEmail,
            utcNow,
            cancellationToken);
    }

    private async Task<Result<StartSocialLoginResponse>>
        HandleLinkedUserAsync(
            User user,
            StartSocialLoginCommand request,
            DateTime utcNow,
            CancellationToken cancellationToken)
    {
        if (!IsEligibleSocialUser(user))
        {
            return SocialLoginErrors.AuthenticationFailed;
        }

        if (user.Status == UserStatus.PendingVerification)
        {
            GeneratedAccessToken restrictedToken =
                _tokenService.GenerateRestrictedVerificationToken(
                    user,
                    utcNow);

            user.UpdateLastLogin(utcNow);

            await _dbContext.SaveChangesAsync(
                cancellationToken);

            return new StartSocialLoginResponse(
                AuthAccessType.RestrictedVerification,
                restrictedToken.PlainTextToken,
                restrictedToken.ExpiresOnUtc,
                RefreshToken: null,
                RefreshTokenExpiresOnUtc: null,
                DeviceSessionId: null,
                OpaqueChallenge: null);
        }

        if (user.Status != UserStatus.Active)
        {
            return SocialLoginErrors.AuthenticationFailed;
        }

        int activeSessionCount =
            await _dbContext.DeviceSessions
                .CountAsync(
                    session =>
                        session.UserId == user.Id &&
                        session.RevokedOnUtc == null &&
                        session.ExpiresOnUtc > utcNow,
                    cancellationToken);

        if (activeSessionCount >=
            DeviceSessionPolicy.MaximumActiveSessions)
        {
            return SocialLoginErrors.SessionLimitReached;
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

        return new StartSocialLoginResponse(
            AuthAccessType.Normal,
            accessToken.PlainTextToken,
            accessToken.ExpiresOnUtc,
            refreshToken.PlainTextToken,
            refreshToken.ExpiresOnUtc,
            deviceSession.Id,
            OpaqueChallenge: null);
    }

    private async Task<Result<StartSocialLoginResponse>>
        CreateExistingEmailChallengeAsync(
            User user,
            ExternalLoginProvider provider,
            string providerSubject,
            Email verifiedEmail,
            DateTime utcNow,
            CancellationToken cancellationToken)
    {
        GeneratedOtpCode generatedOtp =
            _otpService.Generate(
                OtpPolicy.CodeLength);

        VerificationRequest otpRequest =
            VerificationRequest.Create(
                user.Id,
                verifiedEmail.Value,
                generatedOtp.Hash,
                VerificationChannel.Email,
                VerificationPurpose.ConfirmExternalLoginLink,
                utcNow,
                utcNow.Add(OtpPolicy.Lifetime));

        SocialAuthenticationChallenge challenge =
            new(
                provider,
                providerSubject,
                verifiedEmail.Value,
                user.Id,
                otpRequest.Id,
                utcNow.Add(
                    SocialLoginPolicy.ChallengeLifetime));

        string opaqueChallenge =
            await _challengeStore.CreateAsync(
                challenge,
                cancellationToken);

        _dbContext.VerificationRequests.Add(
            otpRequest);

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        await _emailSender.SendVerificationCodeAsync(
            verifiedEmail.Value,
            generatedOtp.PlainTextCode,
            VerificationPurpose.ConfirmExternalLoginLink,
            cancellationToken);

        return CreateChallengeResponse(
            opaqueChallenge);
    }

    private async Task<Result<StartSocialLoginResponse>>
        CreateNewSocialUserChallengeAsync(
            ExternalLoginProvider provider,
            string providerSubject,
            Email? verifiedEmail,
            DateTime utcNow,
            CancellationToken cancellationToken)
    {
        SocialAuthenticationChallenge challenge =
            new(
                provider,
                providerSubject,
                verifiedEmail?.Value,
                ExistingUserId: null,
                LinkVerificationRequestId: null,
                utcNow.Add(
                    SocialLoginPolicy.ChallengeLifetime));

        string opaqueChallenge =
            await _challengeStore.CreateAsync(
                challenge,
                cancellationToken);

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        return CreateChallengeResponse(
            opaqueChallenge);
    }

    private static StartSocialLoginResponse
        CreateChallengeResponse(
            string opaqueChallenge)
    {
        return new StartSocialLoginResponse(
            AccessType: null,
            AccessToken: null,
            AccessTokenExpiresOnUtc: null,
            RefreshToken: null,
            RefreshTokenExpiresOnUtc: null,
            DeviceSessionId: null,
            OpaqueChallenge: opaqueChallenge);
    }

    private static bool IsEligibleSocialUser(
        User user)
    {
        return user.Accounts.Any(
            account =>
                account.AccountType !=
                AccountType.Elderly);
    }

    private static bool TryNormalizeExternalIdentity(
        ExternalLoginProvider requestedProvider,
        VerifiedExternalIdentity? externalIdentity,
        out string providerSubject,
        out Email? verifiedEmail)
    {
        providerSubject = string.Empty;
        verifiedEmail = null;

        if (externalIdentity is null ||
            externalIdentity.Provider !=
                requestedProvider ||
            string.IsNullOrWhiteSpace(
                externalIdentity.ProviderSubject))
        {
            return false;
        }

        providerSubject =
            externalIdentity.ProviderSubject.Trim();

        if (providerSubject.Length >
            UserExternalLogin.MaximumProviderSubjectLength)
        {
            return false;
        }

        if (externalIdentity.VerifiedEmail is null)
        {
            return true;
        }

        try
        {
            verifiedEmail = Email.Create(
                externalIdentity.VerifiedEmail);

            return true;
        }
        catch (DomainException)
        {
            return false;
        }
    }
}