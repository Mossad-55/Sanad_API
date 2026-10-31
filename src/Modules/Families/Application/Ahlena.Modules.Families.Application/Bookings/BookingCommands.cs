using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Idempotency;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Enums;
using Ahlena.BuildingBlocks.Domain.Exceptions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.BuildingBlocks.Domain.ValueObjects;
using Ahlena.BuildingBlocks.Infrastructure.Persistence;
using Ahlena.Modules.Families.Application.Abstractions.Bookings;
using Ahlena.Modules.Families.Application.Abstractions.Caregivers;
using Ahlena.Modules.Families.Application.Abstractions.Data;
using Ahlena.Modules.Families.Application.Abstractions.Payments;
using Ahlena.Modules.Families.Domain.Bookings;
using Ahlena.Modules.Families.Domain.Families;
using Ahlena.Modules.Finance.Application;
using IdempotencyRecord = Ahlena.Modules.Families.Domain.Idempotency.IdempotencyRequest;

namespace Ahlena.Modules.Families.Application.Bookings;

// ----------------------------- Responses -----------------------------

public sealed record BookingCheckoutResponse(
    Guid BookingId,
    BookingStatus Status,
    decimal TotalPayableAmount,
    string Currency);

public sealed record FamilyBookingListItemResponse(
    Guid Id,
    Guid CaregiverId,
    string? CaregiverArabicName,
    string? CaregiverEnglishName,
    string? CaregiverAvatarUrl,
    string? SeniorArabicName,
    string? SeniorEnglishName,
    DateOnly BookingDate,
    TimeOnly StartTime,
    TimeOnly EndTime,
    BookingShiftType ShiftType,
    BookingStatus Status,
    decimal TotalPayableAmount,
    string Currency);

// --------------------------- Checkout Command -------------------------

public sealed record CreateBookingCheckoutCommand(
    UserId UserId,
    ElderlyId ElderlyId,
    CaregiverId CaregiverId,
    BookingShiftType ShiftType,
    DateOnly BookingDate,
    TimeOnly StartTime,
    TimeOnly EndTime,
    string ServiceAddress,
    string? SpecialInstructions,
    DateOnly CurrentDate,
    DateTime UtcNow,
    Guid? BookingAreaId = null,
    Guid? BookingServiceId = null,
    IdempotencyRequest? Idempotency = null)
    : ICommand<IdempotentOutcome<BookingCheckoutResponse>>;

public sealed class CreateBookingCheckoutCommandHandler
    : ICommandHandler<CreateBookingCheckoutCommand, IdempotentOutcome<BookingCheckoutResponse>>
{
    private const int AcceptanceWindowHours = 24;

    private static readonly Error ScheduleConflict = new(
        "Bookings.ScheduleConflict",
        "Caregiver is already booked for this time slot.");

    private static readonly Error AvailabilityConflict = new(
        "Bookings.AvailabilityConflict",
        "The requested time is outside the caregiver's declared availability.");

    private static readonly Error IdempotencyConflict = new(
        "Bookings.IdempotencyConflict",
        "This Idempotency-Key was already used with a different request.");

    private readonly IFamiliesDbContext _dbContext;
    private readonly ICaregiverBookingPricing _caregiverBookingPricing;
    private readonly IPlatformChargeRuleReader _chargeRules;
    private readonly IPlatformControlReader _controls;
    private readonly IIdempotentResponseSnapshotter _snapshotter;
    private readonly ICaregiverAvailabilityRules _availabilityRules;
    private readonly ICaregiverBookingCheckoutGuard _checkoutGuard;
    private readonly ICaregiverCatalogLookup? _catalogLookup;

    public CreateBookingCheckoutCommandHandler(
        IFamiliesDbContext dbContext,
        ICaregiverBookingPricing caregiverBookingPricing,
        IPlatformChargeRuleReader chargeRules,
        IPlatformControlReader controls,
        IIdempotentResponseSnapshotter snapshotter,
        ICaregiverAvailabilityRules availabilityRules,
        ICaregiverBookingCheckoutGuard checkoutGuard,
        ICaregiverCatalogLookup? catalogLookup = null)
    {
        _dbContext = dbContext;
        _caregiverBookingPricing = caregiverBookingPricing;
        _chargeRules = chargeRules;
        _controls = controls;
        _snapshotter = snapshotter;
        _availabilityRules = availabilityRules;
        _checkoutGuard = checkoutGuard;
        _catalogLookup = catalogLookup;
    }

    public async Task<Result<IdempotentOutcome<BookingCheckoutResponse>>> Handle(
        CreateBookingCheckoutCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            IdempotencyRequest? idempotency = request.Idempotency;

            // E04: a new caregiver booking checkout is gated before any
            // validation, slot reservation or persistence, so a rejection
            // cannot create a booking or any partial payment state. With an
            // Idempotency-Key the gate answer is held until after the lookup,
            // so a confirmed replay answers its original result instead of a
            // maintenance 503 (a replay is historical, not a new offer).
            Error? platformError = await PlatformControlGate.CheckNewCheckoutAsync(
                _controls,
                cancellationToken);

            if (platformError is not null && idempotency is null)
            {
                return Result<IdempotentOutcome<BookingCheckoutResponse>>.Failure(platformError);
            }

            // 1. Resolve Family & Verify Member Role
            var family = await _dbContext.Families
                .Include(f => f.Members)
                .SingleOrDefaultAsync(f => f.Members.Any(m => m.Id == request.UserId), cancellationToken);

            if (family is null)
            {
                return Result<IdempotentOutcome<BookingCheckoutResponse>>.Failure(
                    new Error("Bookings.FamilyNotFound", "Family account not found for current user."));
            }

            FamilyRole? role = family.GetRole(request.UserId);
            if (role is not (FamilyRole.Owner or FamilyRole.Editor))
            {
                return Result<IdempotentOutcome<BookingCheckoutResponse>>.Failure(
                    new Error("Bookings.UnauthorizedRole", "Viewers are not permitted to create bookings."));
            }

            // 2. Verify Elderly belongs to this Family
            bool elderlyExists = await _dbContext.Elderlies
                .AnyAsync(e => e.Id == request.ElderlyId && e.FamilyId == family.Id, cancellationToken);

            if (!elderlyExists)
            {
                return Result<IdempotentOutcome<BookingCheckoutResponse>>.Failure(
                    new Error("Bookings.ElderlyNotFound", "Elderly dependent does not belong to this family."));
            }

            // Authorization is re-checked before any replay: only the
            // authenticated actor's own operation+key can be replayed.
            if (idempotency is not null)
            {
                IdempotencyRecord? stored = await FindStoredAsync(
                    request.UserId,
                    idempotency.Key.Value,
                    cancellationToken);

                if (stored is not null)
                {
                    return Replay(stored, idempotency);
                }

                if (platformError is not null)
                {
                    return Result<IdempotentOutcome<BookingCheckoutResponse>>.Failure(platformError);
                }
            }

            // 3b. Pricing attribution (Chat Plus): both booked dimensions
            // together or neither; both must be active catalogue entries
            // selected by the booked caregiver at creation. Shape violations
            // and unknown/inactive/unselected pairs are the owning booking
            // vocabulary's 400, never a silent unattributed booking.
            if (request.BookingAreaId.HasValue != request.BookingServiceId.HasValue)
            {
                return Result<IdempotentOutcome<BookingCheckoutResponse>>.Failure(
                    new Error("Bookings.AttributionInvalid", "Booked area and service must be supplied together."));
            }

            if (request.BookingAreaId.HasValue && request.BookingServiceId.HasValue)
            {
                Guid areaId = request.BookingAreaId.Value;
                Guid serviceId = request.BookingServiceId.Value;

                if (areaId == Guid.Empty || serviceId == Guid.Empty)
                {
                    return Result<IdempotentOutcome<BookingCheckoutResponse>>.Failure(
                        new Error("Bookings.AttributionInvalid", "Booked area and service cannot be empty."));
                }

                // Fail closed: without the catalogue seam there is no way
                // to prove the pair is active and selected by the booked
                // caregiver, so an attributed checkout cannot be accepted.
                if (_catalogLookup is null)
                {
                    return Result<IdempotentOutcome<BookingCheckoutResponse>>.Failure(
                        new Error("Bookings.AttributionInvalid", "Booked area and service cannot be verified."));
                }

                bool selected = await _catalogLookup.IsSelectedPairAsync(
                    request.CaregiverId,
                    areaId,
                    serviceId,
                    cancellationToken);

                if (!selected)
                {
                    return Result<IdempotentOutcome<BookingCheckoutResponse>>.Failure(
                        new Error("Bookings.AttributionInvalid", "Booked area and service must be active and selected by the caregiver."));
                }
            }

            // 4. Server-side pricing — never trust a client-supplied fee (Section 13.1)
            Result<CaregiverBookingPrice> price = await _caregiverBookingPricing.GetBookingPriceAsync(
                request.CaregiverId,
                request.ShiftType,
                request.StartTime,
                request.EndTime,
                cancellationToken);

            if (!price.IsSuccess)
            {
                return Result<IdempotentOutcome<BookingCheckoutResponse>>.Failure(price.Error);
            }

            // 5. Acceptance window = min(now + 24h, booking start)
            DateTime bookingStartUtc = request.BookingDate.ToDateTime(request.StartTime);
            DateTime acceptanceDeadline =
                request.UtcNow.AddHours(AcceptanceWindowHours) <= bookingStartUtc
                    ? request.UtcNow.AddHours(AcceptanceWindowHours)
                    : bookingStartUtc;

            // 6. Immutable price snapshot (family pays base + platform fee)
            var charge = await _chargeRules.GetEffectiveAsync(request.UtcNow, cancellationToken);
            if (charge is null)
                return Result<IdempotentOutcome<BookingCheckoutResponse>>.Failure(new Error("Bookings.ChargesNotConfigured", "Shared platform fee and tax configuration is not available."));
            BookingPriceSnapshot priceSnapshot = BookingPriceSnapshot.Calculate(price.Value.BaseFee, charge.PlatformFeeRatePercentage, charge.TaxRatePercentage, charge.Version);

            // 7. The slot check and the insert are serialized per caregiver
            // and date. The existing overlap predicate is unchanged and stays
            // inside the guard; availability is the additive gate.
            return await _checkoutGuard.ExecuteAsync(
                request.CaregiverId,
                request.BookingDate,
                async () =>
                {
                    // Existing conflict rule (pending bookings reserve the slot too)
                    bool hasConflict = await _dbContext.Bookings.AnyAsync(
                        b => b.CaregiverId == request.CaregiverId &&
                             b.BookingDate == request.BookingDate &&
                             (b.Status == BookingStatus.PendingPayment ||
                              b.Status == BookingStatus.PendingCaregiverApproval ||
                              b.Status == BookingStatus.Confirmed ||
                              b.Status == BookingStatus.InProgress) &&
                             request.StartTime < b.EndTime &&
                             request.EndTime > b.StartTime,
                        cancellationToken);

                    if (hasConflict)
                    {
                        return Result<IdempotentOutcome<BookingCheckoutResponse>>.Failure(
                            ScheduleConflict);
                    }

                    bool isAvailable = await _availabilityRules.IsAvailableAsync(
                        request.CaregiverId,
                        request.BookingDate,
                        request.ShiftType,
                        request.StartTime,
                        request.EndTime,
                        cancellationToken);

                    if (!isAvailable)
                    {
                        return Result<IdempotentOutcome<BookingCheckoutResponse>>.Failure(
                            AvailabilityConflict);
                    }

                    // 8. Create Booking Aggregate
                    Booking booking = Booking.Create(
                        family.Id,
                        request.UserId,
                        request.ElderlyId,
                        request.CaregiverId,
                        price.Value.CaregiverType,
                        request.ShiftType,
                        request.BookingDate,
                        request.StartTime,
                        request.EndTime,
                        request.ServiceAddress,
                        request.SpecialInstructions,
                        priceSnapshot,
                        acceptanceDeadline,
                        request.CurrentDate,
                        request.UtcNow,
                        request.BookingAreaId,
                        request.BookingServiceId);

                    var response = new BookingCheckoutResponse(
                        booking.Id.Value,
                        booking.Status,
                        priceSnapshot.TotalPayableAmount,
                        priceSnapshot.Currency);

                    _dbContext.Bookings.Add(booking);

                    if (idempotency is not null)
                    {
                        // The row commits atomically with the booking: a
                        // rejected attempt never stores a key.
                        _dbContext.IdempotencyRequests.Add(
                            IdempotencyRecord.Create(
                                request.UserId,
                                IdempotencyOperations.FamilyBookingCheckout,
                                idempotency.Key.Value,
                                idempotency.Fingerprint,
                                booking.Id.Value,
                                200,
                                _snapshotter.Snapshot(response),
                                request.UtcNow));
                    }

                    await _dbContext.SaveChangesAsync(cancellationToken);

                    return Result<IdempotentOutcome<BookingCheckoutResponse>>.Success(
                        IdempotentOutcome<BookingCheckoutResponse>.Fresh(
                            response,
                            200));
                },
                cancellationToken);
        }
        catch (CaregiverBookingScheduleConflictException)
        {
            // The serializable transaction could not be serialized: the slot
            // outcome is unknown, never proof that the slot is unavailable.
            return Result<IdempotentOutcome<BookingCheckoutResponse>>.Failure(
                ScheduleConflict);
        }
        catch (DbUpdateException exception) when (
            request.Idempotency is not null &&
            PostgresErrors.IsUniqueViolation(
                exception,
                IdempotencyRecord.UniqueIndexName))
        {
            // A concurrent duplicate won the unique (actor, operation, key)
            // index. Detach the losing rows and replay the winner.
            _dbContext.DetachAdded<Booking>();
            _dbContext.DetachAdded<IdempotencyRecord>();

            IdempotencyRecord? winner = await FindStoredAsync(
                request.UserId,
                request.Idempotency.Key.Value,
                cancellationToken);

            if (winner is null)
            {
                throw;
            }

            return Replay(winner, request.Idempotency);
        }
        catch (DomainException exception)
        {
            return Result<IdempotentOutcome<BookingCheckoutResponse>>.Failure(
                new Error("Bookings.Domain.InvalidOperation", exception.Message));
        }
    }

    private async Task<IdempotencyRecord?> FindStoredAsync(
        UserId actor,
        string key,
        CancellationToken cancellationToken) =>
        await _dbContext.IdempotencyRequests
            .AsNoTracking()
            .SingleOrDefaultAsync(
                row => row.ActorId == actor &&
                    row.Operation == IdempotencyOperations.FamilyBookingCheckout &&
                    row.Key == key,
                cancellationToken);

    private static Result<IdempotentOutcome<BookingCheckoutResponse>> Replay(
        IdempotencyRecord stored,
        IdempotencyRequest idempotency) =>
        stored.Fingerprint == idempotency.Fingerprint
            ? Result<IdempotentOutcome<BookingCheckoutResponse>>.Success(
                IdempotentOutcome<BookingCheckoutResponse>.Replay(
                    stored.ResponseStatus,
                    stored.ResponseBody))
            : Result<IdempotentOutcome<BookingCheckoutResponse>>.Failure(
                IdempotencyConflict);
}

// --------------------------- Cancel Command ------------------------------

public sealed record CancelBookingCommand(
    BookingId BookingId,
    UserId UserId,
    string? Reason,
    int? ReasonCategory,
    DateTime UtcNow) : ICommand;

public sealed class CancelBookingCommandHandler : ICommandHandler<CancelBookingCommand>
{
    private readonly IFamiliesDbContext _dbContext;
    private readonly IPaymobClient _paymobClient;
    private readonly IBookingCancellationFactRecorder _cancellationFactRecorder;

    public CancelBookingCommandHandler(
        IFamiliesDbContext dbContext,
        IPaymobClient paymobClient,
        IBookingCancellationFactRecorder cancellationFactRecorder)
    {
        _dbContext = dbContext;
        _paymobClient = paymobClient;
        _cancellationFactRecorder = cancellationFactRecorder;
    }

    public async Task<Result> Handle(
        CancelBookingCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            // 1. Resolve family by membership (as before)
            var family = await _dbContext.Families
                .AsNoTracking()
                .Include(f => f.Members)
                .SingleOrDefaultAsync(f => f.Members.Any(m => m.Id == request.UserId), cancellationToken);

            if (family is null)
            {
                return Result.Failure(
                    new Error("Bookings.FamilyNotFound", "Family account not found for current user."));
            }

            // 2. Role gate: only Owner/Editor may cancel (Viewers rejected, same pattern as checkout)
            FamilyRole? role = family.GetRole(request.UserId);
            if (role is not (FamilyRole.Owner or FamilyRole.Editor))
            {
                return Result.Failure(
                    new Error("Bookings.UnauthorizedRole", "Viewers are not permitted to cancel bookings."));
            }

            Booking? booking = await _dbContext.Bookings
                .SingleOrDefaultAsync(b => b.Id == request.BookingId, cancellationToken);

            if (booking is null)
            {
                return Result.Failure(new Error("Bookings.NotFound", "Booking not found."));
            }

            if (booking.FamilyId != family.Id)
            {
                return Result.Failure(
                    new Error("Bookings.BookingNotInFamily", "Booking was not found in this family."));
            }

            // 3. Feedback shape validation — before any domain mutation
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
            else if (request.ReasonCategory is not null
                && booking.Status is (BookingStatus.PendingPayment or BookingStatus.PendingCaregiverApproval))
            {
                // Pre-acceptance: no category may be invented
                return Result.Failure(
                    new Error(
                        "Bookings.Cancel.ReasonCategoryNotAllowedPreAcceptance",
                        "A reason category may not be supplied before the booking is accepted."));
            }
            else
            {
                // Pre-acceptance: optional plain note (blank normalizes to null).
                // Any other status falls through to the policy below, which refuses it.
                feedback = BookingCancellationFeedback.CreateOptionalNote(request.Reason);
            }

            // 4. Policy decision first — every rejection happens on an untouched aggregate. The
            //    booking's sold policy revision decides: legacy bookings keep the v1
            //    acceptance-relative rule, revision-2 bookings use the scheduled-start policy and
            //    the richer amount-bearing decision.
            BookingCancellationPolicyInput legacyInput = BookingCancellationPolicyInput.FromBooking(
                booking,
                BookingCancellationActorSide.Family,
                BookingCancellationAction.Cancel,
                request.UtcNow,
                feedback);

            BookingRefundPolicyInput scheduledStartInput = BookingRefundPolicyInput.ForFamilyCancellation(
                booking,
                request.UtcNow,
                feedback,
                allocations: booking.SessionAllocations.Count > 0
                    ? booking.SessionAllocations
                    : null);

            (BookingRefundDecision? scheduledStartDecision, BookingCancellationDecision? legacyDecision) =
                BookingRefundPolicyDispatch.Decide(booking, legacyInput, scheduledStartInput);

            // The award is read from whichever decision the revision produced, so the money moved
            // can never disagree with the revision the booking was sold under. The entitlement
            // itself travels on the decision and onto the recorded fact.
            long awardedMinorUnits = scheduledStartDecision?.AwardedMinorUnits
                ?? LegacyAwardMinorUnits(legacyDecision!, booking);

            // 5. State transition — the aggregate's status guard stays authoritative
            booking.CancelByFamily(feedback?.Note ?? string.Empty, request.UtcNow);

            // 6. Record exactly one fact BEFORE the external call, so the award the policy owed is
            //    durable even if the process dies between the claim and the provider response.
            BookingCancellationFact fact = scheduledStartDecision is not null
                ? BookingCancellationFact.Create(booking.Id, request.UserId, scheduledStartDecision, feedback)
                : BookingCancellationFact.Create(booking.Id, request.UserId, legacyDecision!);
            await _cancellationFactRecorder.RecordAsync(fact, cancellationToken);

            // 7. Claim the refund attempt and COMMIT before any external call. This commit is what
            //    makes the attempt exclusive: a competing request holding a stale concurrency token
            //    fails its own save here and therefore never reaches the provider.
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
                // Another request claimed this booking's refund first; its attempt owns the
                // movement, so this one must not send anything.
                return Result.Failure(
                    new Error(
                        "Bookings.Cancel.AlreadyProcessed",
                        "This booking's refund was claimed by another request."));
            }

            // 8. Provider send, only after the claim (and the award it carries) is durable. A zero
            //    award moves no money and records nothing, which the executor handles itself.
            BookingRefundSettlement settlement = await BookingRefundSettlementExecutor.SettleWithProviderAsync(
                booking,
                awardedMinorUnits,
                _paymobClient,
                request.UtcNow,
                cancellationToken,
                claimId);

            // 9. Final save — the recorded outcome of the claimed attempt. Losing the row to a
            //    competing writer (typically an operator reconciliation that ran during the
            //    provider call) must never discard a real settlement: the helper re-reads the
            //    durable award and treats an already-recorded movement as idempotent success.
            if (!await BookingRefundPersistence.TrySaveClaimedOutcomeAsync(
                    _dbContext,
                    booking.Id,
                    settlement.SettledMinorUnits,
                    cancellationToken))
            {
                return Result.Failure(BookingRefundSettlementErrors.SettlementIncomplete);
            }

            // Legacy tolerance on this endpoint: a gateway REFUSAL does not fail the cancellation —
            // the booking transitioned and the fact records what the policy owed, so the caller
            // still sees success and the outstanding settlement is retried by ops. An UNCONFIRMED
            // outcome is not tolerated: the booking now carries the decision #17 marker and is
            // surfaced as a failure rather than reported as done.
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
            // The refund claim (or the cancellation itself) was committed by a competing request.
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

    /// <summary>
    /// The piastre award of a legacy (revision-1) decision. Revision 1 is amount-free by design —
    /// it decides only "full captured refund or nothing" — so a full entitlement maps to the whole
    /// captured amount and a denial to zero. The captured amount is read through the aggregate's
    /// public refund ceiling (captured less previous refunds), exactly as the caregiver cancel path
    /// reads it: revision 1 can never award a partial, so the two values coincide, and a booking
    /// whose capture cannot be resolved yields zero rather than an invented amount.
    /// </summary>
    private static long LegacyAwardMinorUnits(
        BookingCancellationDecision decision,
        Booking booking)
    {
        if (decision.RefundEntitlement != BookingRefundEntitlement.FullCapturedRefund)
            return 0;

        return booking.RefundableCeilingMinorUnits();
    }
}
