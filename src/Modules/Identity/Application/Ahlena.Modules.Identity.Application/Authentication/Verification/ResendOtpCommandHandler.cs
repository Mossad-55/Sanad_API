using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.Abstractions;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.Modules.Identity.Application.Abstractions.Data;
using Ahlena.Modules.Identity.Application.Abstractions.Messaging;
using Ahlena.Modules.Identity.Application.Abstractions.Security;
using Ahlena.Modules.Identity.Domain.Authentication.VerificationRequests;

namespace Ahlena.Modules.Identity.Application.Authentication.Verification;

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
                VerificationStatus.Pending &&
            currentRequest.Status !=
                VerificationStatus.Expired)
        {
            return ResendOtpErrors
                .RequestNotPending;
        }

        VerificationRequest? latestResendableRequest =
            await _dbContext
                .VerificationRequests
                .Where(item =>
                    item.UserId ==
                        currentRequest.UserId &&
                    item.Target ==
                        currentRequest.Target &&
                    item.Purpose ==
                        currentRequest.Purpose &&
                    (item.Status ==
                        VerificationStatus.Pending ||
                     item.Status ==
                        VerificationStatus.Expired))
                .OrderByDescending(item =>
                    item.CreatedOnUtc)
                .ThenByDescending(item =>
                    item.Id)
                .FirstOrDefaultAsync(
                    cancellationToken);

        if (latestResendableRequest is not null &&
            latestResendableRequest.Id !=
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

        try
        {
            await SendCodeAsync(
                replacementRequest,
                generatedOtp.PlainTextCode,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return ResendOtpErrors
                .ProviderDeliveryFailed;
        }

        currentRequest.InvalidateForResend(
            utcNow);

        _dbContext.VerificationRequests.Add(
            replacementRequest);

        try
        {
            await _dbContext.SaveChangesAsync(
                cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ResendOtpErrors
                .RequestSuperseded;
        }

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