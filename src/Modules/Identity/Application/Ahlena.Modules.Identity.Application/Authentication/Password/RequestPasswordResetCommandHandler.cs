using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.Abstractions;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.ValueObjects;
using Ahlena.Modules.Identity.Application.Abstractions.Data;
using Ahlena.Modules.Identity.Application.Abstractions.Messaging;
using Ahlena.Modules.Identity.Application.Abstractions.Security;
using Ahlena.Modules.Identity.Domain.Authentication.VerificationRequests;
using Ahlena.Modules.Identity.Domain.Users;

namespace Ahlena.Modules.Identity.Application.Authentication.Password;

public sealed class RequestPasswordResetCommandHandler :
    ICommandHandler<RequestPasswordResetCommand>
{
    private readonly IIdentityDbContext _dbContext;
    private readonly IOtpService _otpService;
    private readonly IEmailSender _emailSender;
    private readonly IDateTimeProvider _dateTimeProvider;

    public RequestPasswordResetCommandHandler(
        IIdentityDbContext dbContext,
        IOtpService otpService,
        IEmailSender emailSender,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _otpService = otpService;
        _emailSender = emailSender;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result> Handle(
        RequestPasswordResetCommand request,
        CancellationToken cancellationToken)
    {
        Email email =
            Email.Create(
                request.Email);

        User? user =
            await _dbContext.Users
                .SingleOrDefaultAsync(
                    item =>
                        item.Email ==
                        email,
                    cancellationToken);

        if (user is null ||
            !user.HasPassword ||
            user.Status !=
                UserStatus.Active)
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
                    item.Purpose ==
                        VerificationPurpose
                            .ResetPassword &&
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

        VerificationRequest resetRequest =
            VerificationRequest.Create(
                user.Id,
                email.Value,
                generatedOtp.Hash,
                VerificationChannel.Email,
                VerificationPurpose.ResetPassword,
                utcNow,
                expiresOnUtc);

        _dbContext.VerificationRequests.Add(
            resetRequest);

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        await _emailSender.SendVerificationCodeAsync(
            email.Value,
            generatedOtp.PlainTextCode,
            VerificationPurpose.ResetPassword,
            cancellationToken);

        return Result.Success();
    }
}