using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Application.Abstractions.Data;
using Sanad.Modules.Identity.Application.Abstractions.Messaging;
using Sanad.Modules.Identity.Application.Abstractions.Security;
using Sanad.Modules.Identity.Application.Authentication;
using Sanad.Modules.Identity.Application.Authentication.Registration;
using Sanad.Modules.Identity.Domain.Authentication.VerificationRequests;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.Modules.Identity.Application.Users;

public sealed class UpdateMyProfileCommandHandler :
    ICommandHandler<
        UpdateMyProfileCommand,
        UpdateMyProfileResponse>
{
    private readonly IIdentityDbContext _dbContext;
    private readonly IOtpService _otpService;
    private readonly IEmailSender _emailSender;
    private readonly ISmsSender _smsSender;
    private readonly IDateTimeProvider _dateTimeProvider;

    public UpdateMyProfileCommandHandler(
        IIdentityDbContext dbContext,
        IOtpService otpService,
        IEmailSender emailSender,
        ISmsSender smsSender,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _otpService = otpService;
        _emailSender = emailSender;
        _smsSender = smsSender;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<UpdateMyProfileResponse>> Handle(
        UpdateMyProfileCommand request,
        CancellationToken cancellationToken)
    {
        User? user =
            await _dbContext.Users
                .SingleOrDefaultAsync(
                    item =>
                        item.Id ==
                        request.CurrentUserId,
                    cancellationToken);

        if (user is null)
        {
            return Result<UpdateMyProfileResponse>.Failure(
                AccountErrors.UserNotFound);
        }

        if (user.Status ==
            UserStatus.Blocked)
        {
            return Result<UpdateMyProfileResponse>.Failure(
                AccountErrors.InvalidOperation);
        }

        DateTime utcNow =
            _dateTimeProvider.UtcNow;

        ChangeNames(
            user,
            request,
            utcNow);

        VerificationRequest? emailVerification = null;
        GeneratedOtpCode? emailOtp = null;

        Email? newEmail =
            ResolveNewEmail(
                user,
                request);

        if (newEmail is not null)
        {
            bool emailExists =
                await _dbContext.Users
                    .AnyAsync(
                        item =>
                            item.Email == newEmail &&
                            item.Id != user.Id,
                        cancellationToken);

            if (emailExists)
            {
                return Result<UpdateMyProfileResponse>.Failure(
                    RegistrationErrors.EmailAlreadyInUse);
            }

            user.ChangeEmail(
                newEmail,
                utcNow);

            emailOtp = _otpService.Generate(
                OtpPolicy.CodeLength);

            emailVerification = VerificationRequest.Create(
                user.Id,
                newEmail.Value,
                emailOtp.Hash,
                VerificationChannel.Email,
                VerificationPurpose.VerifyEmail,
                utcNow,
                utcNow.Add(
                    OtpPolicy.Lifetime));

            _dbContext.VerificationRequests.Add(
                emailVerification);
        }

        VerificationRequest? phoneVerification = null;
        GeneratedOtpCode? phoneOtp = null;

        PhoneNumber? newPhoneNumber =
            ResolveNewPhoneNumber(
                user,
                request);

        if (newPhoneNumber is not null)
        {
            bool phoneExists =
                await _dbContext.Users
                    .AnyAsync(
                        item =>
                            item.PhoneNumber ==
                                newPhoneNumber &&
                            item.Id != user.Id,
                        cancellationToken);

            if (phoneExists)
            {
                return Result<UpdateMyProfileResponse>.Failure(
                    RegistrationErrors.PhoneAlreadyInUse);
            }

            user.ChangePhoneNumber(
                newPhoneNumber,
                utcNow);

            phoneOtp = _otpService.Generate(
                OtpPolicy.CodeLength);

            phoneVerification = VerificationRequest.Create(
                user.Id,
                newPhoneNumber.Value,
                phoneOtp.Hash,
                VerificationChannel.Sms,
                VerificationPurpose.VerifyPhone,
                utcNow,
                utcNow.Add(
                    OtpPolicy.Lifetime));

            _dbContext.VerificationRequests.Add(
                phoneVerification);
        }

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        List<Task> otpSends =
        [];

        if (emailVerification is not null &&
            emailOtp is not null)
        {
            otpSends.Add(
                _emailSender.SendVerificationCodeAsync(
                    emailVerification.Target,
                    emailOtp.PlainTextCode,
                    VerificationPurpose.VerifyEmail,
                    cancellationToken));
        }

        if (phoneVerification is not null &&
            phoneOtp is not null)
        {
            otpSends.Add(
                _smsSender.SendVerificationCodeAsync(
                    phoneVerification.Target,
                    phoneOtp.PlainTextCode,
                    VerificationPurpose.VerifyPhone,
                    cancellationToken));
        }

        if (otpSends.Count > 0)
        {
            await Task.WhenAll(
                otpSends);
        }

        return new UpdateMyProfileResponse(
            user.EmailVerified,
            user.PhoneVerified,
            emailVerification?.Id,
            phoneVerification?.Id);
    }

    private static void ChangeNames(
        User user,
        UpdateMyProfileCommand request,
        DateTime utcNow)
    {
        bool hasArabicFullName =
            !string.IsNullOrWhiteSpace(
                request.ArabicFullName);

        bool hasEnglishFullName =
            !string.IsNullOrWhiteSpace(
                request.EnglishFullName);

        if (!hasArabicFullName &&
            !hasEnglishFullName)
        {
            return;
        }

        FullName arabicFullName =
            hasArabicFullName
                ? FullName.Create(
                    request.ArabicFullName!)
                : user.ArabicFullName;

        FullName englishFullName =
            hasEnglishFullName
                ? FullName.Create(
                    request.EnglishFullName!)
                : user.EnglishFullName;

        user.ChangeName(
            arabicFullName,
            englishFullName,
            utcNow);
    }

    private static Email? ResolveNewEmail(
        User user,
        UpdateMyProfileCommand request)
    {
        if (string.IsNullOrWhiteSpace(
            request.Email))
        {
            return null;
        }

        Email email =
            Email.Create(
                request.Email);

        if (email == user.Email)
        {
            return null;
        }

        return email;
    }

    private static PhoneNumber? ResolveNewPhoneNumber(
        User user,
        UpdateMyProfileCommand request)
    {
        if (string.IsNullOrWhiteSpace(
            request.PhoneNumber))
        {
            return null;
        }

        PhoneNumber phoneNumber =
            PhoneNumber.Create(
                request.PhoneNumber);

        if (phoneNumber == user.PhoneNumber)
        {
            return null;
        }

        return phoneNumber;
    }
}
