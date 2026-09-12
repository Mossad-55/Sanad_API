using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Invitations;

namespace Sanad.Modules.Families.Application.Families;

public sealed record DeleteFamilyCommand(
    UserId CallerUserId,
    string? Reason,
    string? OptionalMessage,
    bool Acknowledgement)
    : ICommand;

public sealed class DeleteFamilyCommandValidator : AbstractValidator<DeleteFamilyCommand>
{
    public DeleteFamilyCommandValidator()
    {
        RuleFor(c => c.CallerUserId).NotEqual(UserId.Empty);
    }
}

public sealed class DeleteFamilyCommandHandler : ICommandHandler<DeleteFamilyCommand>
{
    private readonly IFamiliesDbContext _dbContext;

    public DeleteFamilyCommandHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result> Handle(
        DeleteFamilyCommand request,
        CancellationToken cancellationToken)
    {
        Family? family = await FamilyAccess.ResolveFamilyAsync(
            _dbContext,
            request.CallerUserId,
            cancellationToken);

        if (family is null)
        {
            return Result.Failure(FamilyErrors.NotFound);
        }

        FamilyRole? role = family.GetRole(request.CallerUserId);

        if (role is null)
        {
            return Result.Failure(FamilyErrors.AccessDenied);
        }

        if (role != FamilyRole.Owner)
        {
            return Result.Failure(FamilyErrors.NotOwner);
        }

        if (!request.Acknowledgement)
        {
            return Result.Failure(FamilyErrors.AcknowledgementRequired);
        }

        // Blocking rules: active booking or unsettled payment
        List<Booking> bookings = await _dbContext.Bookings
            .Include(booking => booking.PaymentTransactions)
            .Where(booking => booking.FamilyId == family.Id)
            .ToListAsync(cancellationToken);

        if (bookings.Any(b => b.Status is BookingStatus.PendingPayment
                                or BookingStatus.PendingCaregiverApproval
                                or BookingStatus.Confirmed
                                or BookingStatus.InProgress))
        {
            return Result.Failure(FamilyErrors.ActiveBookingExists);
        }

        if (bookings.SelectMany(b => b.PaymentTransactions)
            .Any(t => t.Status == PaymentTransactionStatus.Pending))
        {
            return Result.Failure(FamilyErrors.UnsettledPaymentExists);
        }

        List<Elderly> dependents = await _dbContext.Elderlies
            .Where(elderly => elderly.FamilyId == family.Id)
            .ToListAsync(cancellationToken);

        foreach (Elderly dependent in dependents)
        {
            dependent.Anonymize();
        }

        List<FamilyInvitation> pendingInvitations = await _dbContext.Invitations
            .Where(invitation => invitation.FamilyId == family.Id
                                 && invitation.Status == FamilyInvitationStatus.Pending)
            .ToListAsync(cancellationToken);

        DateTime utcNow = DateTime.UtcNow;

        foreach (FamilyInvitation invitation in pendingInvitations)
        {
            invitation.Revoke(utcNow);
        }

        family.MarkDeleted(request.Reason, request.OptionalMessage);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}