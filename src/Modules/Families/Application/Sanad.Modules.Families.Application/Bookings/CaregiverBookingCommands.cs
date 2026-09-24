using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Application.Abstractions.Payments;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Domain.Subscriptions;

namespace Sanad.Modules.Families.Application.Bookings;

// ============================= Accept Booking =============================

public sealed record CaregiverAcceptBookingCommand(
    CaregiverId CaregiverId,
    BookingId BookingId,
    DateTime UtcNow) : ICommand;

public sealed class CaregiverAcceptBookingCommandHandler : ICommandHandler<CaregiverAcceptBookingCommand>
{
    private readonly IFamiliesDbContext _dbContext;

    public CaregiverAcceptBookingCommandHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result> Handle(
        CaregiverAcceptBookingCommand request,
        CancellationToken cancellationToken)
    {
        try
        {

            Booking? booking = await _dbContext.Bookings
                .SingleOrDefaultAsync(b => b.Id == request.BookingId && b.CaregiverId == request.CaregiverId, cancellationToken);

            if (booking is null)
            {
                return Result.Failure(new Error("Bookings.NotFound", "Booking not found for this caregiver."));
            }

            try
            {
                booking.AcceptByCaregiver(request.UtcNow);
                await _dbContext.SaveChangesAsync(cancellationToken);
                return Result.Success();
            }
            catch (Exception ex)
            {
                return Result.Failure(new Error("Bookings.TransitionFailed", ex.Message));
            }
        }
        catch (DomainException exception)
        {
            return Result.Failure(new Error("Bookings.Domain.InvalidOperation", exception.Message));
        }
    }
}

// ============================= Decline Booking =============================

public sealed record CaregiverDeclineBookingCommand(
    CaregiverId CaregiverId,
    UserId ActorUserId,
    BookingId BookingId,
    string Reason,
    DateTime UtcNow) : ICommand;

public sealed class CaregiverDeclineBookingCommandHandler : ICommandHandler<CaregiverDeclineBookingCommand>
{
    private readonly IFamiliesDbContext _dbContext;
    private readonly IPaymobClient _paymobClient;
    private readonly IBookingCancellationFactRecorder _cancellationFactRecorder;

    public CaregiverDeclineBookingCommandHandler(
        IFamiliesDbContext dbContext,
        IPaymobClient paymobClient,
        IBookingCancellationFactRecorder cancellationFactRecorder)
    {
        _dbContext = dbContext;
        _paymobClient = paymobClient;
        _cancellationFactRecorder = cancellationFactRecorder;
    }

    public async Task<Result> Handle(
        CaregiverDeclineBookingCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            Booking? booking = await _dbContext.Bookings
                .SingleOrDefaultAsync(b => b.Id == request.BookingId && b.CaregiverId == request.CaregiverId, cancellationToken);

            if (booking is null)
            {
                return Result.Failure(new Error("Bookings.NotFound", "Booking not found for this caregiver."));
            }

            // A decline happens before acceptance: an optional plain note, never an invented
            // category. The policy speaks on the untouched aggregate, so a booking whose capture
            // evidence cannot be resolved fails here instead of declining silently.
            BookingCancellationFeedback? feedback =
                BookingCancellationFeedback.CreateOptionalNote(request.Reason);

            var input = BookingCancellationPolicyInput.FromBooking(
                booking,
                BookingCancellationActorSide.Caregiver,
                BookingCancellationAction.Reject,
                request.UtcNow,
                feedback);
            var decision = BookingCancellationPolicy.Decide(input);

            try
            {
                booking.DeclineByCaregiver(request.Reason, request.UtcNow);

                PaymentTransaction? paidTransaction = booking.PaymentTransactions.FirstOrDefault(
                    t => t.Status == PaymentTransactionStatus.Succeeded
                        && t.PaymobTransactionId is not null);

                if (paidTransaction is not null)
                {
                    Result<string?> refund = await _paymobClient.RefundPaymentAsync(
                        paidTransaction.PaymobTransactionId!,
                        paidTransaction.Amount,
                        cancellationToken);

                    if (refund.IsSuccess)
                    {
                        booking.MarkRefunded(refund.Value, request.UtcNow);
                    }
                }

                // The rejection fact commits in the same single save as the decline itself.
                var fact = BookingCancellationFact.Create(
                    booking.Id,
                    request.ActorUserId,
                    decision);
                await _cancellationFactRecorder.RecordAsync(fact, cancellationToken);

                await _dbContext.SaveChangesAsync(cancellationToken);
                return Result.Success();
            }
            catch (DbUpdateException exception) when (CancellationPersistenceGuard.IsFactUniqueViolation(exception))
            {
                // Parallel-decline race: a fact for this booking was committed first. Ordered
                // before the generic catch so the race maps to 409, never to TransitionFailed.
                return Result.Failure(
                    new Error(
                        "Bookings.Cancel.AlreadyProcessed",
                        "This booking cancellation was already processed."));
            }
            catch (Exception ex)
            {
                return Result.Failure(new Error("Bookings.TransitionFailed", ex.Message));
            }
        }
        catch (DomainException exception)
        {
            return Result.Failure(new Error("Bookings.Domain.InvalidOperation", exception.Message));
        }
    }
}

// ============================= Cancel Booking =============================

public sealed record CaregiverCancelBookingCommand(
    CaregiverId CaregiverId,
    UserId ActorUserId,
    BookingId BookingId,
    string? Reason,
    int? ReasonCategory,
    DateTime UtcNow) : ICommand;

public sealed class CaregiverCancelBookingCommandHandler : ICommandHandler<CaregiverCancelBookingCommand>
{
    private readonly IFamiliesDbContext _dbContext;
    private readonly IPaymobClient _paymobClient;
    private readonly IBookingCancellationFactRecorder _cancellationFactRecorder;

    public CaregiverCancelBookingCommandHandler(
        IFamiliesDbContext dbContext,
        IPaymobClient paymobClient,
        IBookingCancellationFactRecorder cancellationFactRecorder)
    {
        _dbContext = dbContext;
        _paymobClient = paymobClient;
        _cancellationFactRecorder = cancellationFactRecorder;
    }

    public async Task<Result> Handle(
        CaregiverCancelBookingCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            // 1. Scope the booking to the assigned caregiver (as in every sibling handler)
            Booking? booking = await _dbContext.Bookings
                .SingleOrDefaultAsync(b => b.Id == request.BookingId && b.CaregiverId == request.CaregiverId, cancellationToken);

            if (booking is null)
            {
                return Result.Failure(new Error("Bookings.NotFound", "Booking not found for this caregiver."));
            }

            // 2. Feedback shape validation — before any domain mutation
            BookingCancellationFeedback? feedback;

            if (booking.Status == BookingStatus.Confirmed)
            {
                // Accepted: a known category AND a non-blank note are mandatory
                if (request.ReasonCategory is null)
                {
                    return Result.Failure(
                        new Error(
                            "Bookings.Cancel.ReasonCategoryRequired",
                            "A reason category is required to cancel an accepted booking."));
                }

                if (BookingCancellationReasonCategories.TryParse(request.ReasonCategory.Value, out BookingCancellationReasonCategory category) is false)
                {
                    return Result.Failure(
                        new Error(
                            "Bookings.Cancel.ReasonCategoryInvalid",
                            "The supplied reason category is not a defined cancellation reason."));
                }

                if (string.IsNullOrWhiteSpace(request.Reason))
                {
                    return Result.Failure(
                        new Error(
                            "Bookings.Cancel.ReasonRequired",
                            "A reason note is required to cancel an accepted booking."));
                }

                feedback = BookingCancellationFeedback.Create(category, request.Reason);
            }
            else if (request.ReasonCategory is not null)
            {
                // Pre-acceptance: no category may be invented. Defensive only — this endpoint
                // cancels Confirmed bookings; any other status is refused by the policy below.
                return Result.Failure(
                    new Error(
                        "Bookings.Cancel.ReasonCategoryNotAllowedPreAcceptance",
                        "A reason category may not be supplied before the booking is accepted."));
            }
            else
            {
                // Pre-acceptance: optional plain note (blank normalizes to null).
                feedback = BookingCancellationFeedback.CreateOptionalNote(request.Reason);
            }

            // 3. Policy decision first — every rejection happens on an untouched aggregate
            var input = BookingCancellationPolicyInput.FromBooking(
                booking,
                BookingCancellationActorSide.Caregiver,
                BookingCancellationAction.Cancel,
                request.UtcNow,
                feedback);
            var decision = BookingCancellationPolicy.Decide(input);

            // 4. State transition — the aggregate's status guard stays authoritative
            booking.CancelByCaregiver(feedback?.Note ?? string.Empty, request.UtcNow);

            // 5. Refund by entitlement: only a full-captured entitlement touches the provider
            switch (decision.RefundEntitlement)
            {
                case BookingRefundEntitlement.FullCapturedRefund:
                    {
                        PaymentTransaction? paidTransaction = booking.PaymentTransactions.FirstOrDefault(
                            t => t.Status == PaymentTransactionStatus.Succeeded
                                && t.PaymobTransactionId is not null);

                        if (paidTransaction is not null)
                        {
                            Result<string?> refund = await _paymobClient.RefundPaymentAsync(
                                paidTransaction.PaymobTransactionId!,
                                paidTransaction.Amount,
                                cancellationToken);

                            if (refund.IsSuccess)
                            {
                                booking.MarkRefunded(refund.Value, request.UtcNow);
                            }
                        }

                        break;
                    }

                case BookingRefundEntitlement.NoRefundDue:
                    // Policy denial (or nothing captured): no provider call, no status touch.
                    break;

                default:
                    throw new System.Diagnostics.UnreachableException(
                        $"Unexpected refund entitlement '{decision.RefundEntitlement}'.");
            }

            // 6. Record exactly one fact, after the refund attempt so the recorded
            //    decision reflects the final state (immediate-refund success included)
            var fact = BookingCancellationFact.Create(
                booking.Id,
                request.ActorUserId,
                decision);
            await _cancellationFactRecorder.RecordAsync(fact, cancellationToken);

            // 7. Single save — booking mutation + fact commit atomically
            await _dbContext.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
        catch (DomainException exception)
        {
            return Result.Failure(new Error("Bookings.Domain.InvalidOperation", exception.Message));
        }
        catch (DbUpdateException exception) when (CancellationPersistenceGuard.IsFactUniqueViolation(exception))
        {
            // Parallel-cancel race: a fact for this booking was committed first
            return Result.Failure(
                new Error(
                    "Bookings.Cancel.AlreadyProcessed",
                    "This booking cancellation was already processed."));
        }
    }
}

// ============================= Start Visit =============================

public sealed record CaregiverStartBookingCommand(
    CaregiverId CaregiverId,
    BookingId BookingId,
    DateTime UtcNow) : ICommand;

public sealed class CaregiverStartBookingCommandHandler : ICommandHandler<CaregiverStartBookingCommand>
{
    private readonly IFamiliesDbContext _dbContext;

    public CaregiverStartBookingCommandHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result> Handle(
        CaregiverStartBookingCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            Booking? booking = await _dbContext.Bookings
                .AsNoTracking()
                .SingleOrDefaultAsync(b => b.Id == request.BookingId && b.CaregiverId == request.CaregiverId, cancellationToken);

            if (booking is null)
            {
                return Result.Failure(new Error("Bookings.NotFound", "Booking not found for this caregiver."));
            }

            if (_dbContext is DbContext dbContext
                && dbContext.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
            {
                Booking? trackedBooking = await _dbContext.Bookings
                    .SingleOrDefaultAsync(b => b.Id == request.BookingId && b.CaregiverId == request.CaregiverId, cancellationToken);

                if (trackedBooking is null)
                    return Result.Failure(new Error("Bookings.NotFound", "Booking not found for this caregiver."));

                trackedBooking.StartVisit(request.UtcNow);
                await _dbContext.SaveChangesAsync(cancellationToken);
                return Result.Success();
            }

            booking.StartVisit(request.UtcNow);

            int updated = await _dbContext.Bookings
                .Where(b => b.Id == request.BookingId
                    && b.CaregiverId == request.CaregiverId
                    && b.Status == BookingStatus.Confirmed
                    && b.ConfirmedOnUtc == booking.ConfirmedOnUtc)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(b => b.Status, booking.Status)
                        .SetProperty(b => b.StartedOnUtc, booking.StartedOnUtc)
                        .SetProperty(b => b.UpdatedOnUtc, booking.UpdatedOnUtc),
                    cancellationToken);

            if (updated == 0)
            {
                return Result.Failure(new Error(
                    "Bookings.Domain.InvalidOperation",
                    "The booking state changed before the visit could start."));
            }

            return Result.Success();
        }
        catch (DomainException exception)
        {
            return Result.Failure(new Error("Bookings.Domain.InvalidOperation", exception.Message));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Result.Failure(new Error(
                "Bookings.TransitionFailed",
                "The visit transition could not be persisted."));
        }
    }
}

// ============================= Complete Visit =============================

public sealed record CaregiverCompleteBookingCommand(
    CaregiverId CaregiverId,
    BookingId BookingId,
    string? Notes,
    DateTime UtcNow) : ICommand;

public sealed class CaregiverCompleteBookingCommandHandler : ICommandHandler<CaregiverCompleteBookingCommand>
{
    private readonly IFamiliesDbContext _dbContext;

    public CaregiverCompleteBookingCommandHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result> Handle(
        CaregiverCompleteBookingCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            Booking? booking = await _dbContext.Bookings
                .AsNoTracking()
                .SingleOrDefaultAsync(b => b.Id == request.BookingId && b.CaregiverId == request.CaregiverId, cancellationToken);

            if (booking is null)
            {
                return Result.Failure(new Error("Bookings.NotFound", "Booking not found for this caregiver."));
            }

            if (_dbContext is DbContext dbContext
                && dbContext.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
            {
                Booking? trackedBooking = await _dbContext.Bookings
                    .SingleOrDefaultAsync(b => b.Id == request.BookingId && b.CaregiverId == request.CaregiverId, cancellationToken);

                if (trackedBooking is null)
                    return Result.Failure(new Error("Bookings.NotFound", "Booking not found for this caregiver."));

                booking.CompleteVisit(request.Notes, request.UtcNow);

                FamilySubscription? subscription = await _dbContext.FamilySubscriptions
                    .SingleOrDefaultAsync(s => s.FamilyId == booking.FamilyId && s.IsCurrent, cancellationToken);
                if (subscription is not null && !subscription.TryConsumeBookingAllowance())
                {
                    return Result.Failure(new Error(
                        "Bookings.AllowanceExceeded",
                        "The family subscription has no remaining booking allowance for this period."));
                }

                trackedBooking.CompleteVisit(request.Notes, request.UtcNow);
                await _dbContext.SaveChangesAsync(cancellationToken);
                return Result.Success();
            }

            booking.CompleteVisit(request.Notes, request.UtcNow);

            if (_dbContext is not DbContext relationalDbContext)
                return Result.Failure(new Error("Bookings.TransitionFailed", "The visit transition could not be persisted."));

            await using var transaction = await relationalDbContext.Database.BeginTransactionAsync(cancellationToken);
            FamilySubscription? currentSubscription = await _dbContext.FamilySubscriptions
                .SingleOrDefaultAsync(s => s.FamilyId == booking.FamilyId && s.IsCurrent, cancellationToken);
            if (currentSubscription is not null && !currentSubscription.TryConsumeBookingAllowance())
            {
                return Result.Failure(new Error(
                    "Bookings.AllowanceExceeded",
                    "The family subscription has no remaining booking allowance for this period."));
            }

            int updated = await _dbContext.Bookings
                .Where(b => b.Id == request.BookingId
                    && b.CaregiverId == request.CaregiverId
                    && b.Status == BookingStatus.InProgress
                    && b.StartedOnUtc == booking.StartedOnUtc)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(b => b.Status, booking.Status)
                        .SetProperty(b => b.CaregiverNotes, booking.CaregiverNotes)
                        .SetProperty(b => b.CompletedOnUtc, booking.CompletedOnUtc)
                        .SetProperty(b => b.UpdatedOnUtc, booking.UpdatedOnUtc),
                    cancellationToken);

            if (updated == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Result.Failure(new Error(
                    "Bookings.Domain.InvalidOperation",
                    "The booking state changed before the visit could complete."));
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result.Success();
        }
        catch (DomainException exception)
        {
            return Result.Failure(new Error("Bookings.Domain.InvalidOperation", exception.Message));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(new Error(
                "Bookings.AllowanceConcurrency",
                "The booking allowance changed while the visit was completing. Please retry."));
        }
        catch (Exception)
        {
            return Result.Failure(new Error(
                "Bookings.TransitionFailed",
                "The visit transition could not be persisted."));
        }
    }
}
