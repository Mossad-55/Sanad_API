using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Application.Abstractions.Data;
using Sanad.Modules.Identity.Application.Abstractions.Messaging;
using Sanad.Modules.Identity.Application.Abstractions.Security;
using Sanad.Modules.Identity.Domain.Authentication.VerificationRequests;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.Modules.Identity.Application.Authentication.SocialLogin;

public sealed class RequestSocialRegistrationOtpCommandHandler :
    ICommandHandler<
        RequestSocialRegistrationOtpCommand,
        RequestSocialRegistrationOtpResponse>
{
    private readonly IIdentityDbContext _dbContext;
    private readonly ISocialAuthenticationChallengeStore
        _socialAuthenticationChallengeStore;
    private readonly ISocialRegistrationChallengeStore
        _socialRegistrationChallengeStore;
    private readonly IOtpService _otpService;
    private readonly ISmsSender _smsSender;
    private readonly IDateTimeProvider _dateTimeProvider;

    public RequestSocialRegistrationOtpCommandHandler(
        IIdentityDbContext dbContext,
        ISocialAuthenticationChallengeStore
            socialAuthenticationChallengeStore,
        ISocialRegistrationChallengeStore
            socialRegistrationChallengeStore,
        IOtpService otpService,
        ISmsSender smsSender,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _socialAuthenticationChallengeStore =
            socialAuthenticationChallengeStore;
        _socialRegistrationChallengeStore =
            socialRegistrationChallengeStore;
        _otpService = otpService;
        _smsSender = smsSender;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<
        Result<RequestSocialRegistrationOtpResponse>> Handle(
            RequestSocialRegistrationOtpCommand request,
            CancellationToken cancellationToken)
    {
        DateTime utcNow =
            _dateTimeProvider.UtcNow;

        SocialAuthenticationChallenge? socialChallenge =
            await _socialAuthenticationChallengeStore
                .GetActiveAsync(
                    request.OpaqueChallenge,
                    utcNow,
                    cancellationToken);

        if (!TryGetNewUserChallenge(
            socialChallenge,
            out Email verifiedEmail))
        {
            return SocialLoginErrors
                .SocialRegistrationFailed;
        }

        FullName arabicFullName;
        FullName englishFullName;
        PhoneNumber phoneNumber;

        try
        {
            arabicFullName =
                FullName.Create(
                    request.ArabicFullName);

            englishFullName =
                FullName.Create(
                    request.EnglishFullName);

            phoneNumber =
                PhoneNumber.Create(
                    request.PhoneNumber);
        }
        catch (DomainException)
        {
            return SocialLoginErrors
                .SocialRegistrationFailed;
        }

        if (!IsSupportedAccountType(
            request.AccountType))
        {
            return SocialLoginErrors
                .SocialRegistrationFailed;
        }

        bool emailOrPhoneAlreadyExists =
            await _dbContext.Users.AnyAsync(
                user =>
                    user.Email == verifiedEmail ||
                    user.PhoneNumber == phoneNumber,
                cancellationToken);

        if (emailOrPhoneAlreadyExists)
        {
            return SocialLoginErrors
                .SocialRegistrationFailed;
        }

        GeneratedOtpCode generatedOtp =
            _otpService.Generate(
                OtpPolicy.CodeLength);

        VerificationRequest phoneVerificationRequest =
            VerificationRequest.Create(
                userId: null,
                phoneNumber.Value,
                generatedOtp.Hash,
                VerificationChannel.Sms,
                VerificationPurpose.VerifyPhone,
                utcNow,
                utcNow.Add(
                    OtpPolicy.Lifetime));

        SocialRegistrationChallenge registrationChallenge =
            new(
                socialChallenge!.Provider,
                socialChallenge.ProviderSubject,
                verifiedEmail.Value,
                arabicFullName.Value,
                englishFullName.Value,
                request.AccountType,
                phoneNumber.Value,
                phoneVerificationRequest.Id,
                utcNow.Add(
                    SocialLoginPolicy.ChallengeLifetime));

        string opaqueRegistrationChallenge =
            await _socialRegistrationChallengeStore
                .CreateAsync(
                    registrationChallenge,
                    cancellationToken);

        _dbContext.VerificationRequests.Add(
            phoneVerificationRequest);

        bool consumptionStaged =
            await _socialAuthenticationChallengeStore
                .StageConsumeAsync(
                    request.OpaqueChallenge,
                    utcNow,
                    cancellationToken);

        if (!consumptionStaged)
        {
            return SocialLoginErrors
                .SocialRegistrationFailed;
        }

        try
        {
            await _dbContext.SaveChangesAsync(
                cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return SocialLoginErrors
                .SocialRegistrationFailed;
        }

        await _smsSender.SendVerificationCodeAsync(
            phoneNumber.Value,
            generatedOtp.PlainTextCode,
            VerificationPurpose.VerifyPhone,
            cancellationToken);

        return new RequestSocialRegistrationOtpResponse(
            opaqueRegistrationChallenge,
            registrationChallenge.ExpiresOnUtc);
    }

    private static bool TryGetNewUserChallenge(
        SocialAuthenticationChallenge? challenge,
        out Email verifiedEmail)
    {
        verifiedEmail = default!;

        if (challenge is null ||
            challenge.ExistingUserId.HasValue ||
            challenge.LinkVerificationRequestId.HasValue ||
            !challenge.EmailIsAuthoritative ||
            string.IsNullOrWhiteSpace(
                challenge.VerifiedEmail))
        {
            return false;
        }

        try
        {
            verifiedEmail = Email.Create(
                challenge.VerifiedEmail);

            return true;
        }
        catch (DomainException)
        {
            return false;
        }
    }

    private static bool IsSupportedAccountType(
        AccountType accountType)
    {
        return accountType is
            AccountType.Family or
            AccountType.MedicalCaregiver or
            AccountType.CompanionCaregiver;
    }
}