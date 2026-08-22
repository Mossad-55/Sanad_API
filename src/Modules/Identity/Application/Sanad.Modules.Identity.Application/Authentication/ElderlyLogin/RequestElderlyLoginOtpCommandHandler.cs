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

namespace Sanad.Modules.Identity.Application.Authentication.ElderlyLogin;

public sealed class RequestElderlyLoginOtpCommandHandler :
    ICommandHandler<RequestElderlyLoginOtpCommand>
{
    private readonly IIdentityDbContext _dbContext;
    private readonly IOtpService _otpService;
    private readonly ISmsSender _smsSender;
    private readonly IDateTimeProvider _dateTimeProvider;

    public RequestElderlyLoginOtpCommandHandler(
        IIdentityDbContext dbContext,
        IOtpService otpService,
        ISmsSender smsSender,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _otpService = otpService;
        _smsSender = smsSender;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result> Handle(
        RequestElderlyLoginOtpCommand request,
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
            return Result.Success();
        }

        DateTime utcNow =
            _dateTimeProvider.UtcNow;

        VerificationRequest? pendingRequest =
            await _dbContext.VerificationRequests
                .Where(item =>
                    item.UserId ==
                        user.Id &&
                    item.Target ==
                        phoneNumber.Value &&
                    item.Purpose ==
                        VerificationPurpose.ElderlyLogin &&
                    item.Status ==
                        VerificationStatus.Pending)
                .OrderByDescending(item =>
                    item.CreatedOnUtc)
                .FirstOrDefaultAsync(
                    cancellationToken);

        if (pendingRequest is not null &&
            utcNow <
                pendingRequest.CreatedOnUtc.Add(
                    OtpPolicy.ResendCooldown))
        {
            return Result.Success();
        }

        if (pendingRequest is not null)
        {
            pendingRequest.Invalidate(
                utcNow);
        }

        GeneratedOtpCode generatedOtp =
            _otpService.Generate(
                OtpPolicy.CodeLength);

        DateTime expiresOnUtc =
            utcNow.Add(
                OtpPolicy.Lifetime);

        VerificationRequest verificationRequest =
            VerificationRequest.Create(
                user.Id,
                phoneNumber.Value,
                generatedOtp.Hash,
                VerificationChannel.Sms,
                VerificationPurpose.ElderlyLogin,
                utcNow,
                expiresOnUtc);

        _dbContext.VerificationRequests.Add(
            verificationRequest);

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        await _smsSender.SendVerificationCodeAsync(
            phoneNumber.Value,
            generatedOtp.PlainTextCode,
            VerificationPurpose.ElderlyLogin,
            cancellationToken);

        return Result.Success();
    }

    private static bool IsEligibleElderlyUser(
        User user)
    {
        return (user.Status is
                UserStatus.PendingVerification or
                UserStatus.Active &&
            user.Accounts.Count == 1 &&
            user.Accounts.Single().AccountType ==
                AccountType.Elderly);
    }
}