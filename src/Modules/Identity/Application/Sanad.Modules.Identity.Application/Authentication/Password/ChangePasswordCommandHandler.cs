using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Identity.Application.Abstractions.Data;
using Sanad.Modules.Identity.Application.Abstractions.Security;
using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.Modules.Identity.Application.Authentication.Password;

public sealed class ChangePasswordCommandHandler :
    ICommandHandler<ChangePasswordCommand>
{
    private readonly IIdentityDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IDateTimeProvider _dateTimeProvider;

    public ChangePasswordCommandHandler(
        IIdentityDbContext dbContext,
        IPasswordHasher passwordHasher,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result> Handle(
        ChangePasswordCommand request,
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

        PasswordVerificationResult currentPasswordVerification = _passwordHasher.Verify(
            password.PasswordHash,
            request.CurrentPassword);

        if (currentPasswordVerification == PasswordVerificationResult.Failed)
        {
            return Result.Failure(PasswordErrors
                .InvalidCurrentPassword);
        }

        PasswordVerificationResult newPasswordVerification = _passwordHasher.Verify(
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

        user.ChangePasswordHash(
            newPasswordHash,
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
                "Password was changed.",
                utcNow);
        }

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        return Result.Success();
    }
}