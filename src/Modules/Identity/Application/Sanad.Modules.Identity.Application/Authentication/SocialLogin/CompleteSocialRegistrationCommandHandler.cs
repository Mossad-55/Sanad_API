using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Application.Abstractions.Data;
using Sanad.Modules.Identity.Application.Abstractions.Security;
using Sanad.Modules.Identity.Application.Authentication.Tokens;
using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;
using Sanad.Modules.Identity.Domain.Authentication.ExternalLogins;
using Sanad.Modules.Identity.Domain.Authentication.VerificationRequests;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.Modules.Identity.Application.Authentication.SocialLogin;

public sealed class CompleteSocialRegistrationCommandHandler :
    ICommandHandler<CompleteSocialRegistrationCommand, StartSocialLoginResponse>
{
    private readonly IIdentityDbContext _dbContext;
    private readonly ISocialRegistrationChallengeStore _challengeStore;
    private readonly IOtpService _otpService;
    private readonly IAuthTokenService _tokenService;
    private readonly IDateTimeProvider _dateTimeProvider;

    public CompleteSocialRegistrationCommandHandler(
        IIdentityDbContext dbContext,
        ISocialRegistrationChallengeStore challengeStore,
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
        CompleteSocialRegistrationCommand request,
        CancellationToken cancellationToken)
    {
        DateTime utcNow = _dateTimeProvider.UtcNow;

        SocialRegistrationChallenge? challenge =
            await _challengeStore.ConsumeAsync(
                request.OpaqueRegistrationChallenge,
                utcNow,
                cancellationToken);

        if (!TryNormalizeChallenge(challenge, utcNow,
            out Email email, out PhoneNumber phone,
            out FullName arabicName, out FullName englishName))
        {
            return SocialLoginErrors.SocialRegistrationFailed;
        }

        VerificationRequest? otpRequest =
            await _dbContext.VerificationRequests.SingleOrDefaultAsync(
                item => item.Id == challenge!.PhoneVerificationRequestId,
                cancellationToken);

        if (otpRequest is null ||
            otpRequest.UserId.HasValue ||
            otpRequest.Target != phone.Value ||
            otpRequest.Channel != VerificationChannel.Sms ||
            otpRequest.Purpose != VerificationPurpose.VerifyPhone ||
            otpRequest.Status != VerificationStatus.Pending)
        {
            return SocialLoginErrors.SocialRegistrationFailed;
        }

        if (otpRequest.IsExpired(utcNow))
        {
            otpRequest.MarkExpired(utcNow);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return SocialLoginErrors.SocialRegistrationFailed;
        }

        if (!_otpService.Verify(request.Code, otpRequest.OtpHash))
        {
            otpRequest.RegisterFailedAttempt(utcNow);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return SocialLoginErrors.SocialRegistrationFailed;
        }

        bool duplicateContact = await _dbContext.Users.AnyAsync(
            user => user.Email == email || user.PhoneNumber == phone,
            cancellationToken);

        bool providerSubjectExists = await _dbContext.Users.AnyAsync(
            user => user.ExternalLogins.Any(login =>
                login.Provider == challenge!.Provider &&
                login.ProviderSubject == challenge.ProviderSubject),
            cancellationToken);

        if (duplicateContact || providerSubjectExists)
        {
            return SocialLoginErrors.SocialRegistrationFailed;
        }

        DeviceSessionPolicy.EnsureCanCreateSession(0);

        User user = User.Create(arabicName, englishName, email, phone);
        user.AddAccount(challenge!.AccountType);
        user.LinkExternalLogin(challenge.Provider, challenge.ProviderSubject, utcNow);
        user.VerifyEmail(utcNow);
        user.VerifyPhone(utcNow);
        user.Activate(utcNow);

        GeneratedAccessToken accessToken =
            _tokenService.GenerateAccessToken(user, utcNow);
        GeneratedRefreshToken refreshToken =
            _tokenService.GenerateRefreshToken(utcNow);

        DeviceSession session = DeviceSession.Create(
            user.Id, request.DeviceName, request.DevicePlatform,
            request.AppVersion, refreshToken.Hash, utcNow,
            refreshToken.ExpiresOnUtc);

        otpRequest.Verify(utcNow);
        user.UpdateLastLogin(utcNow);
        _dbContext.Users.Add(user);
        _dbContext.DeviceSessions.Add(session);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new StartSocialLoginResponse(
            AuthAccessType.Normal,
            accessToken.PlainTextToken,
            accessToken.ExpiresOnUtc,
            refreshToken.PlainTextToken,
            refreshToken.ExpiresOnUtc,
            session.Id,
            OpaqueChallenge: null);
    }

    private static bool TryNormalizeChallenge(
        SocialRegistrationChallenge? challenge,
        DateTime utcNow,
        out Email email,
        out PhoneNumber phone,
        out FullName arabicName,
        out FullName englishName)
    {
        email = default!;
        phone = default!;
        arabicName = default!;
        englishName = default!;

        if (challenge is null ||
            challenge.ExpiresOnUtc.Kind != DateTimeKind.Utc ||
            utcNow >= challenge.ExpiresOnUtc ||
            challenge.Provider is not (ExternalLoginProvider.Google or ExternalLoginProvider.Apple) ||
            string.IsNullOrWhiteSpace(challenge.ProviderSubject) ||
            challenge.ProviderSubject.Trim().Length > UserExternalLogin.MaximumProviderSubjectLength ||
            !IsSupportedAccountType(challenge.AccountType))
        {
            return false;
        }

        try
        {
            email = Email.Create(challenge.VerifiedEmail);
            phone = PhoneNumber.Create(challenge.PhoneNumber);
            arabicName = FullName.Create(challenge.ArabicFullName);
            englishName = FullName.Create(challenge.EnglishFullName);
            return true;
        }
        catch (DomainException)
        {
            return false;
        }
    }

    private static bool IsSupportedAccountType(AccountType accountType)
    {
        return accountType is AccountType.Family or
            AccountType.MedicalCaregiver or AccountType.CompanionCaregiver;
    }
}
