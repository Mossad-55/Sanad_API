using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Application.Abstractions.Data;
using Sanad.Modules.Identity.Application.Abstractions.Messaging;
using Sanad.Modules.Identity.Application.Abstractions.Security;
using Sanad.Modules.Identity.Domain.Authentication.VerificationRequests;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.Modules.Identity.Application.Authentication.Registration;

public sealed class RegisterUserCommandHandler :
    ICommandHandler<
        RegisterUserCommand,
        RegisterUserResponse>
{
    private readonly IIdentityDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IOtpService _otpService;
    private readonly IEmailSender _emailSender;
    private readonly ISmsSender _smsSender;
    private readonly IDateTimeProvider _dateTimeProvider;

    public RegisterUserCommandHandler(
        IIdentityDbContext dbContext,
        IPasswordHasher passwordHasher,
        IOtpService otpService,
        IEmailSender emailSender,
        ISmsSender smsSender,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _otpService = otpService;
        _emailSender = emailSender;
        _smsSender = smsSender;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<RegisterUserResponse>> Handle(RegisterUserCommand request, CancellationToken cancellationToken)
    {
        if (!IsSupportedAccountType(
            request.AccountType))
        {
            return RegistrationErrors.UnsupportedAccountType;
        }

        Email email =
            Email.Create(
                request.Email);

        PhoneNumber phoneNumber =
            PhoneNumber.Create(
                request.PhoneNumber);

        FullName arabiFullName =
            FullName.Create(
                request.ArabicFullName);

        FullName englishFullName =
            FullName.Create(
                request.EnglishFullName);

        bool emailExists =
            await _dbContext.Users
                .AnyAsync(
                    user =>
                        user.Email == email,
                    cancellationToken);

        if (emailExists)
        {
            return RegistrationErrors.EmailAlreadyInUse;
        }

        bool phoneExists =
            await _dbContext.Users
                .AnyAsync(
                    user =>
                        user.PhoneNumber == phoneNumber,
                    cancellationToken);

        if (phoneExists)
        {
            return RegistrationErrors.PhoneAlreadyInUse;
        }

        DateTime utcNow = _dateTimeProvider.UtcNow;

        string passwordHash = _passwordHasher.Hash(
            request.Password);

        User user = User.Create(
            arabiFullName,
            englishFullName,
            email,
            phoneNumber,
            NormalizedOptionalAvatar(
                request.AvatarUrl));

        user.AddAccount(
            request.AccountType);

        user.SetInitialPasswordHash(
            passwordHash,
            utcNow);

        GeneratedOtpCode emailOtp = _otpService.Generate(
            OtpPolicy.CodeLength);

        GeneratedOtpCode phoneOtp = _otpService.Generate(
            OtpPolicy.CodeLength);

        DateTime expiresOnUtc = utcNow.Add(
            OtpPolicy.Lifetime);

        VerificationRequest emailVerification = VerificationRequest.Create(
            user.Id,
            email.Value,
            emailOtp.Hash,
            VerificationChannel.Email,
            VerificationPurpose.VerifyEmail,
            utcNow,
            expiresOnUtc);

        VerificationRequest phoneVerification = VerificationRequest.Create(
            user.Id,
            phoneNumber.Value,
            phoneOtp.Hash,
            VerificationChannel.Sms,
            VerificationPurpose.VerifyPhone,
            utcNow,
            expiresOnUtc);

        _dbContext.Users.Add(user);

        _dbContext.VerificationRequests.AddRange(
            emailVerification,
            phoneVerification);

        await _dbContext.SaveChangesAsync(cancellationToken);

        await Task.WhenAll(
            _emailSender.SendVerificationCodeAsync(
                email.Value,
                emailOtp.PlainTextCode,
                VerificationPurpose.VerifyEmail,
                cancellationToken),
            _smsSender.SendVerificationCodeAsync(
                phoneNumber.Value,
                phoneOtp.PlainTextCode,
                VerificationPurpose.VerifyPhone,
                cancellationToken));

        return new RegisterUserResponse(
            user.Id,
            emailVerification.Id,
            phoneVerification.Id);
    }

    private static bool IsSupportedAccountType(
        AccountType accountType)
    {
        return accountType is
            AccountType.Family or
            AccountType.MedicalCaregiver or
            AccountType.CompanionCaregiver;
    }

    private static string? NormalizedOptionalAvatar(
        string? avatarUrl)
    {
        return string.IsNullOrWhiteSpace(
            avatarUrl)
                ? null
                : avatarUrl.Trim();
    }
}