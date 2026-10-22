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

            (BookingRefundDecision? scheduledStartDecision, BookingCancellationDecision? legacyDecision) =
                BookingRefundPolicyDispatch.Decide(
                    booking,
                    BookingCancellationPolicyInput.FromBooking(
                        booking,
                        BookingCancellationActorSide.Caregiver,
                        BookingCancellationAction.Reject,
                        request.UtcNow,
                        feedback),
                    BookingRefundPolicyInput.ForCaregiverRejection(
                        booking,
                        request.UtcNow,
                        feedback,
                        allocations: booking.SessionAllocations.Count > 0
                            ? booking.SessionAllocations
                            : null));

            try
            {
                booking.DeclineByCaregiver(request.Reason, request.UtcNow);

                // Both revisions refund a rejection of an unaccepted booking in full; the
                // revision-2 decision carries the amount, the revision-1 decision only the
                // entitlement, so the revision-1 award is read from the booking's ceiling.
                long awardedMinorUnits = scheduledStartDecision is not null
                    ? scheduledStartDecision.AwardedMinorUnits
                    : legacyDecision!.RefundEntitlement == BookingRefundEntitlement.FullCapturedRefund
                        ? booking.RefundableCeilingMinorUnits()
                        : 0L;

                // The rejection fact is recorded before the external call, so the award is durable
                // even if the process dies between the claim and the provider response.
                BookingCancellationFact fact = scheduledStartDecision is not null
                    ? BookingCancellationFact.Create(
                        booking.Id,
                        request.ActorUserId,
                        scheduledStartDecision,
                        feedback)
                    : BookingCancellationFact.Create(booking.Id, request.ActorUserId, legacyDecision!);
                await _cancellationFactRecorder.RecordAsync(fact, cancellationToken);

                // Claim + commit BEFORE the provider send: the committed claim is what makes the
                // attempt exclusive, because a competing request with a stale concurrency token
                // fails its own save and never reaches the provider.
                Guid? claimId = null;

                if (awardedMinorUnits > 0
                    && BookingRefundSettlementExecutor.HasProviderCapture(booking))
                {
                    claimId = Guid.NewGuid();
                    booking.ClaimRefundAttempt(claimId.Value, awardedMinorUnits, request.UtcNow);
                }

                try
                {
                    await _dbContext.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateConcurrencyException)
                {
                    return Result.Failure(
                        new Error(
                            "Bookings.Cancel.AlreadyProcessed",
                            "This booking's refund was claimed by another request."));
                }

                // The award — never the whole captured amount — is what is sent to the gateway,
                // and the executor is the one place that moves it and records the settled amount.
                BookingRefundSettlement settlement = await BookingRefundSettlementExecutor.SettleWithProviderAsync(
                    booking,
                    awardedMinorUnits,
                    _paymobClient,
                    request.UtcNow,
                    cancellationToken,
                    claimId);

                // A late provider confirmation survives a lost row race: the durable award is
                // re-read and an already-recorded movement is treated as idempotent success.
                if (!await BookingRefundPersistence.TrySaveClaimedOutcomeAsync(
                        _dbContext,
                        booking.Id,
                        settlement.SettledMinorUnits,
                        cancellationToken))
                {
                    return Result.Failure(BookingRefundSettlementErrors.SettlementIncomplete);
                }

                // Same legacy tolerance as the caregiver cancel endpoint: a gateway REFUSAL does
                // not fail the decline — the booking transitioned and the fact records what the
                // policy owed, so ops can retry the outstanding settlement. An UNCONFIRMED outcome
                // is not tolerated: the booking carries the decision #17 marker and is surfaced.
                if (settlement.Destination != BookingRefundDestination.None
                    && !settlement.IsCompleted
                    && booking.IsRefundOutcomeAmbiguous)
                {
                    return Result.Failure(
                        settlement.Error ?? BookingRefundSettlementErrors.AmbiguousOutcome);
                }

                return Result.Success();
            }
            catch (DbUpdateConcurrencyException)
            {
                return Result.Failure(
                    new Error(
                        "Bookings.Cancel.AlreadyProcessed",
                        "This booking decline or its refund was already processed."));
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

            // 3. Policy decision first — every rejection happens on an untouched aggregate. Only the
            //    legacy revision reaches the caregiver-cancel path today: the accepted-booking
            //    caregiver cancellation is a full refund under both revisions, and a revision-2
            //    booking takes the same full award through the scheduled-start policy.
            (BookingRefundDecision? scheduledStartDecision, BookingCancellationDecision? legacyDecision) =
                BookingRefundPolicyDispatch.Decide(
                    booking,
                    BookingCancellationPolicyInput.FromBooking(
                        booking,
                        BookingCancellationActorSide.Caregiver,
                        BookingCancellationAction.Cancel,
                        request.UtcNow,
                        feedback),
                    BookingRefundPolicyInput.ForCaregiverCancellation(
                        booking,
                        request.UtcNow,
                        feedback,
                        allocations: booking.SessionAllocations.Count > 0
                            ? booking.SessionAllocations
                            : null));

            long awardedMinorUnits = scheduledStartDecision?.AwardedMinorUnits ?? 0;

            // A revision-1 decision is amount-free; a full entitlement there means the whole
            // captured amount, so the award is read from the booking's own ceiling.
            if (scheduledStartDecision is null
                && legacyDecision!.RefundEntitlement == BookingRefundEntitlement.FullCapturedRefund)
            {
                awardedMinorUnits = booking.RefundableCeilingMinorUnits();
            }

            // 4. State transition — the aggregate's status guard stays authoritative
            booking.CancelByCaregiver(feedback?.Note ?? string.Empty, request.UtcNow);

            // 5. Record exactly one fact BEFORE the external call, so the award is durable even if
            //    the process dies between the claim and the provider response.
            BookingCancellationFact fact = scheduledStartDecision is not null
                ? BookingCancellationFact.Create(booking.Id, request.ActorUserId, scheduledStartDecision, feedback)
                : BookingCancellationFact.Create(booking.Id, request.ActorUserId, legacyDecision!);
            await _cancellationFactRecorder.RecordAsync(fact, cancellationToken);

            // 6. Claim + commit BEFORE the provider send. The committed claim is what makes the
            //    attempt exclusive: a competing request with a stale concurrency token fails its
            //    own save and never reaches the provider.
            Guid? claimId = null;

            if (awardedMinorUnits > 0 && BookingRefundSettlementExecutor.HasProviderCapture(booking))
            {
                claimId = Guid.NewGuid();
                booking.ClaimRefundAttempt(claimId.Value, awardedMinorUnits, request.UtcNow);
            }

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Result.Failure(
                    new Error(
                        "Bookings.Cancel.AlreadyProcessed",
                        "This booking's refund was claimed by another request."));
            }

            // 7. Provider send, then the final save that records the outcome of the claimed attempt.
            BookingRefundSettlement settlement = await BookingRefundSettlementExecutor.SettleWithProviderAsync(
                booking,
                awardedMinorUnits,
                _paymobClient,
                request.UtcNow,
                cancellationToken,
                claimId);

            // A late provider confirmation survives a lost row race: the durable award is re-read
            // and an already-recorded movement is treated as idempotent success.
            if (!await BookingRefundPersistence.TrySaveClaimedOutcomeAsync(
                    _dbContext,
                    booking.Id,
                    settlement.SettledMinorUnits,
                    cancellationToken))
            {
                return Result.Failure(BookingRefundSettlementErrors.SettlementIncomplete);
            }

            // Legacy tolerance on this endpoint: a gateway REFUSAL does not fail the cancellation —
            // the booking has transitioned and the fact records what the policy owed, so the
            // caller still sees success and ops can retry the outstanding settlement. An
            // UNCONFIRMED outcome is different and is never tolerated: the booking now carries the
            // decision #17 marker, so it is surfaced as a failure rather than reported as done.
            if (settlement.Destination != BookingRefundDestination.None
                && !settlement.IsCompleted
                && booking.IsRefundOutcomeAmbiguous)
            {
                return Result.Failure(
                    settlement.Error ?? BookingRefundSettlementErrors.AmbiguousOutcome);
            }

            return Result.Success();
        }
        catch (DomainException exception)
        {
            return Result.Failure(new Error("Bookings.Domain.InvalidOperation", exception.Message));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(
                new Error(
                    "Bookings.Cancel.AlreadyProcessed",
                    "This booking cancellation or its refund was already processed."));
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
