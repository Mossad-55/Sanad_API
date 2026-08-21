using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Application.Abstractions.Data;
using Sanad.Modules.Identity.Application.Abstractions.Security;
using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;
using Sanad.Modules.Identity.Domain.Authentication.VerificationRequests;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.Modules.Identity.Application.Authentication.Password;

public sealed class ResetPasswordCommandHandler :
    ICommandHandler<ResetPasswordCommand>
{
    private readonly IIdentityDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IOtpService _otpService;
    private readonly IDateTimeProvider _dateTimeProvider;

    public ResetPasswordCommandHandler(
        IIdentityDbContext dbContext,
        IPasswordHasher passwordHasher,
        IOtpService otpService,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _otpService = otpService;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result> Handle(
        ResetPasswordCommand request,
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

        if (user is null)
        {
            return Result.Failure(PasswordErrors
                .UserNotFound);
        }

        if (user.Status !=
            UserStatus.Active)
        {
            return Result.Failure(PasswordErrors
                .UserNotActive);
        }

        var password = user.Password;

        if (!user.HasPassword ||
            password is null)
        {
            return Result.Failure(PasswordErrors
                .UserHasNoPassword);
        }

        DateTime utcNow =
            _dateTimeProvider.UtcNow;

        VerificationRequest? resetRequest =
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

        if (resetRequest is null)
        {
            return Result.Failure(PasswordErrors
                .PendingRequestNotFound);
        }

        if (resetRequest.IsExpired(
            utcNow))
        {
            resetRequest.MarkExpired(
                utcNow);

            await _dbContext.SaveChangesAsync(
                cancellationToken);

            return Result.Failure(PasswordErrors
                .OtpVerificationFailed);
        }

        bool codeIsValid =
            _otpService.Verify(
                request.OtpCode,
                resetRequest.OtpHash);

        if (!codeIsValid)
        {
            resetRequest.RegisterFailedAttempt(
                utcNow);

            await _dbContext.SaveChangesAsync(
                cancellationToken);

            return Result.Failure(PasswordErrors
                .OtpVerificationFailed);
        }

        PasswordVerificationResult newPasswordVerification =
            _passwordHasher.Verify(
                password.PasswordHash,
                request.NewPassword);

        if (newPasswordVerification !=
            PasswordVerificationResult.Failed)
        {
            return Result.Failure(PasswordErrors
                .NewPasswordMustDiffer);
        }

        string newPasswordHash =
            _passwordHasher.Hash(
                request.NewPassword);

        user.ResetPasswordHash(
            newPasswordHash,
            utcNow);

        resetRequest.Verify(
            utcNow);

        DeviceSession[] nonRevokedSessions =
            await _dbContext.DeviceSessions
                .Where(item =>
                    item.UserId ==
                        user.Id &&
                    item.RevokedOnUtc ==
                        null)
                .ToArrayAsync(
                    cancellationToken);

        foreach (
            DeviceSession session
            in nonRevokedSessions)
        {
            session.Revoke(
                "Password was reset.",
                utcNow);
        }

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        return Result.Success();
    }
}