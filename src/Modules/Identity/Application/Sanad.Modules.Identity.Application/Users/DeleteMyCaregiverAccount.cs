using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Application.Abstractions.Caregivers;
using Sanad.Modules.Identity.Application.Abstractions.Data;
using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.Modules.Identity.Application.Users;

public sealed record DeleteMyCaregiverAccountCommand(
    UserId CallerUserId)
    : ICommand;

public sealed class DeleteMyCaregiverAccountCommandValidator
    : AbstractValidator<DeleteMyCaregiverAccountCommand>
{
    public DeleteMyCaregiverAccountCommandValidator()
    {
        RuleFor(c => c.CallerUserId).NotEqual(UserId.Empty);
    }
}

/// <summary>
/// SET-8d — caregiver self-delete (Q1 = Option B). The caller decides which
/// account to delete and this command only ever deletes the CAREGIVER side:
///
///  · PURE caregiver (no Family account) → full deletion: the Identity account
///    is blocked + PII-scrubbed (anonymize & retain, SET-8c) and its sessions
///    are revoked, then the caregiver profile is deactivated.
///  · HYBRID (Family + caregiver) → the caregiver profile is deactivated and
///    the caregiver account types are removed from the user; the login, the
///    Family account and the family membership stay alive (no session
///    revocation). Q2 (family-owner protection) is moot by design: this
///    endpoint never touches the Family side, so no family can be orphaned.
///
/// Nothing is hard-deleted; every row is retained.
/// </summary>
public sealed class DeleteMyCaregiverAccountCommandHandler
    : ICommandHandler<DeleteMyCaregiverAccountCommand>
{
    private const string DeletionSessionRevocationReason =
        "The account was deleted by its owner.";

    private const string CaregiverDeactivationReason =
        "Self-deleted by the account owner.";

    private readonly IIdentityDbContext _dbContext;
    private readonly ICaregiverAccountGateway _caregiverGateway;
    private readonly IDateTimeProvider _dateTimeProvider;

    public DeleteMyCaregiverAccountCommandHandler(
        IIdentityDbContext dbContext,
        ICaregiverAccountGateway caregiverGateway,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _caregiverGateway = caregiverGateway;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result> Handle(
        DeleteMyCaregiverAccountCommand request,
        CancellationToken cancellationToken)
    {
        DateTime utcNow = _dateTimeProvider.UtcNow;

        UserId callerUserId = request.CallerUserId;

        // 1. Load the caller.
        User? user = await _dbContext.Users
            .SingleOrDefaultAsync(
                u => u.Id == callerUserId,
                cancellationToken);

        if (user is null)
        {
            return Result.Failure(AccountErrors.UserNotFound);
        }

        // 2. Resolve the caregiver side + the caller's account types.
        CaregiverAccountInfo? info =
            await _caregiverGateway.GetCaregiverAccountAsync(
                request.CallerUserId,
                cancellationToken);

        bool hasCaregiverType = user.Accounts.Any(
            a => a.AccountType is AccountType.MedicalCaregiver
                              or AccountType.CompanionCaregiver);

        bool hasFamilyType = user.Accounts.Any(
            a => a.AccountType == AccountType.Family);

        if (info is null)
        {
            // 2a. No caregiver profile: a family-only user is out of scope
            // (coded 403); a caregiver account type without a profile is an
            // inconsistency (coded 404).
            if (!hasCaregiverType)
            {
                return Result.Failure(AccountErrors.CaregiverOnly);
            }

            return Result.Failure(
                AccountErrors.CaregiverProfileNotFound);
        }

        if (info.IsDeactivated)
        {
            // 2b. The profile is already gone: a partial hybrid retry only
            // has to finish removing the caregiver account types. Anything
            // else is an idempotent no-op.
            if (hasFamilyType && hasCaregiverType)
            {
                user.RemoveCaregiverAccounts();

                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            return Result.Success();
        }

        // 3. D11 guard — fail-safe: nothing is mutated when the booking
        // lookup itself fails.
        Result<bool> active =
            await _caregiverGateway.HasActiveBookingsAsync(
                info.CaregiverId,
                cancellationToken);

        if (active.IsFailure)
        {
            return Result.Failure(active.Error);
        }

        if (active.Value)
        {
            return Result.Failure(AccountErrors.ActiveBookingExists);
        }

        // 4. PURE mode — full deletion (anonymize & retain). The Blocked
        // check makes a pure partial-failure retry skip straight to step 5.
        if (!hasFamilyType)
        {
            if (user.Status != UserStatus.Blocked)
            {
                user.AnonymizeAndDeactivate(utcNow);

                DeviceSession[] sessions = await _dbContext.DeviceSessions
                    .Where(s => s.UserId == callerUserId && s.RevokedOnUtc == null)
                    .ToArrayAsync(cancellationToken);

                foreach (DeviceSession session in sessions)
                {
                    session.Revoke(
                        DeletionSessionRevocationReason,
                        utcNow);
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        // 5. Deactivate the caregiver profile (retained, never hard-deleted).
        Result deactivate =
            await _caregiverGateway.DeactivateCaregiverAsync(
                info.CaregiverId,
                CaregiverDeactivationReason,
                utcNow,
                cancellationToken);

        if (deactivate.IsFailure)
        {
            return Result.Failure(deactivate.Error);
        }

        // 6. HYBRID mode — drop the caregiver account types only. The login,
        // the Family account and the family membership stay alive, and his
        // family sessions must survive (no revocation).
        if (hasFamilyType)
        {
            user.RemoveCaregiverAccounts();

            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        // 7. Done.
        return Result.Success();
    }
}
