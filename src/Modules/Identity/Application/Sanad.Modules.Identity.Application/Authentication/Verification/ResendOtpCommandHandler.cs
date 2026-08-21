using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Identity.Application.Abstractions.Data;
using Sanad.Modules.Identity.Application.Abstractions.Messaging;
using Sanad.Modules.Identity.Application.Abstractions.Security;
using Sanad.Modules.Identity.Domain.Authentication.VerificationRequests;

namespace Sanad.Modules.Identity.Application.Authentication.Verification;

public sealed class ResendOtpCommandHandler :
    ICommandHandler<
        ResendOtpCommand,
        ResendOtpResponse>
{
    private readonly IIdentityDbContext _dbContext;
    private readonly IOtpService _otpService;
    private readonly IEmailSender _emailSender;
    private readonly ISmsSender _smsSender;
    private readonly IDateTimeProvider _dateTimeProvider;

    public ResendOtpCommandHandler(
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

    public async Task<Result<ResendOtpResponse>> Handle(
        ResendOtpCommand request,
        CancellationToken cancellationToken)
    {
        VerificationRequest? currentRequest =
            await _dbContext
                .VerificationRequests
                .SingleOrDefaultAsync(
                    item =>
                        item.Id ==
                        request.VerificationRequestId,
                    cancellationToken);

        if (currentRequest is null)
        {
            return ResendOtpErrors
                .RequestNotFound;
        }

        if (currentRequest.Status !=
            VerificationStatus.Pending)
        {
            return ResendOtpErrors
                .RequestNotPending;
        }

        VerificationRequest? latestPendingRequest =
            await _dbContext
                .VerificationRequests
                .Where(item =>
                    item.Target ==
                        currentRequest.Target &&
                    item.Purpose ==
                        currentRequest.Purpose &&
                    item.Status ==
                        VerificationStatus.Pending)
                .OrderByDescending(item =>
                    item.CreatedOnUtc)
                .FirstOrDefaultAsync(
                    cancellationToken);

        if (latestPendingRequest is not null &&
            latestPendingRequest.Id !=
            currentRequest.Id)
        {
            return ResendOtpErrors
                .RequestSuperseded;
        }

        DateTime utcNow =
            _dateTimeProvider.UtcNow;

        DateTime cooldownEndsOnUtc =
            currentRequest.CreatedOnUtc.Add(
                OtpPolicy.ResendCooldown);

        if (utcNow < cooldownEndsOnUtc)
        {
            return ResendOtpErrors
                .CooldownActive;
        }

        currentRequest.Invalidate(
            utcNow);

        GeneratedOtpCode generatedOtp =
            _otpService.Generate(
                OtpPolicy.CodeLength);

        DateTime expiresOnUtc =
            utcNow.Add(
                OtpPolicy.Lifetime);

        VerificationRequest replacementRequest =
            VerificationRequest.Create(
                currentRequest.UserId,
                currentRequest.Target,
                generatedOtp.Hash,
                currentRequest.Channel,
                currentRequest.Purpose,
                utcNow,
                expiresOnUtc);

        _dbContext.VerificationRequests.Add(
            replacementRequest);

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        await SendCodeAsync(
            replacementRequest,
            generatedOtp.PlainTextCode,
            cancellationToken);

        return new ResendOtpResponse(
            replacementRequest.Id,
            replacementRequest.ExpiresOnUtc);
    }

    private Task SendCodeAsync(
        VerificationRequest request,
        string plainTextCode,
        CancellationToken cancellationToken)
    {
        return request.Channel switch
        {
            VerificationChannel.Email =>
                _emailSender
                    .SendVerificationCodeAsync(
                        request.Target,
                        plainTextCode,
                        request.Purpose,
                        cancellationToken),

            VerificationChannel.Sms =>
                _smsSender
                    .SendVerificationCodeAsync(
                        request.Target,
                        plainTextCode,
                        request.Purpose,
                        cancellationToken),

            _ => throw new InvalidOperationException(
                "Unsupported verification channel.")
        };
    }
}