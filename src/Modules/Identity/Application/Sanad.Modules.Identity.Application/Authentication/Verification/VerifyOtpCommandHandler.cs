using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Application.Abstractions.Data;
using Sanad.Modules.Identity.Application.Abstractions.Security;
using Sanad.Modules.Identity.Domain.Authentication.VerificationRequests;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.Modules.Identity.Application.Authentication.Verification;

public sealed class VerifyOtpCommandHandler :
    ICommandHandler<
        VerifyOtpCommand,
        VerifyOtpResponse>
{
    private readonly IIdentityDbContext _dbContext;
    private readonly IOtpService _otpService;
    private readonly IDateTimeProvider _dateTimeProvider;

    public VerifyOtpCommandHandler(
        IIdentityDbContext dbContext,
        IOtpService otpService,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _otpService = otpService;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<VerifyOtpResponse>> Handle(
        VerifyOtpCommand request,
        CancellationToken cancellationToken)
    {
        VerificationRequest? verificationRequest =
            await _dbContext
                .VerificationRequests
                .SingleOrDefaultAsync(
                    item =>
                        item.Id ==
                        request.VerificationRequestId,
                    cancellationToken);

        if (verificationRequest is null)
        {
            return VerifyOtpErrors
                .RequestNotFound;
        }

        if (verificationRequest.Status !=
            VerificationStatus.Pending)
        {
            return VerifyOtpErrors
                .RequestNotPending;
        }

        bool isSupportedPurpose =
            verificationRequest.Purpose is
                VerificationPurpose.VerifyEmail or
                VerificationPurpose.VerifyPhone;

        if (!isSupportedPurpose)
        {
            return VerifyOtpErrors
                .UnsupportedPurpose;
        }

        if (!verificationRequest.UserId.HasValue)
        {
            return VerifyOtpErrors
                .UserNotFound;
        }

        UserId userId =
            verificationRequest.UserId.Value;

        User? user =
            await _dbContext.Users
                .SingleOrDefaultAsync(
                    item =>
                        item.Id ==
                        userId,
                    cancellationToken);

        if (user is null)
        {
            return VerifyOtpErrors
                .UserNotFound;
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

            return VerifyOtpErrors
                .RequestExpired;
        }

        bool codeIsValid =
            _otpService.Verify(
                request.Code,
                verificationRequest.OtpHash);

        if (!codeIsValid)
        {
            verificationRequest
                .RegisterFailedAttempt(
                    utcNow);

            await _dbContext.SaveChangesAsync(
                cancellationToken);

            return VerifyOtpErrors
                .InvalidCode;
        }

        verificationRequest.Verify(
            utcNow);

        switch (verificationRequest.Purpose)
        {
            case VerificationPurpose.VerifyEmail:
                user.VerifyEmail(
                    utcNow);
                break;

            case VerificationPurpose.VerifyPhone:
                user.VerifyPhone(
                    utcNow);
                break;
        }

        bool bothChannelsVerified =
            user.EmailVerified &&
            user.PhoneVerified;

        if (bothChannelsVerified &&
            user.Status ==
            UserStatus.PendingVerification)
        {
            user.Activate(
                utcNow);
        }

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        int attemptsRemaining =
            Math.Max(
                0,
                verificationRequest.MaxAttempts -
                verificationRequest.Attempts);

        return new VerifyOtpResponse(
            user.Id,
            user.EmailVerified,
            user.PhoneVerified,
            user.Status ==
                UserStatus.Active,
            attemptsRemaining);
    }
}