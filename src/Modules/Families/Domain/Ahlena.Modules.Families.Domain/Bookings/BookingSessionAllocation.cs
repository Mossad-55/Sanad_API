using Ahlena.BuildingBlocks.Domain.Abstractions;
using Ahlena.BuildingBlocks.Domain.Exceptions;

namespace Ahlena.Modules.Families.Domain.Bookings;

/// <summary>
/// One session of a discounted multi-session care package, carrying the slice of the paid package
/// price that was allocated to it at purchase (Owner decision #5: "Allocate discounted package
/// price to sessions at purchase; allocations sum exactly to the paid package total").
/// <para>
/// <b>Why an allocation exists at all.</b> Clause 3 requires unused sessions to be refunded at
/// their <em>allocated discounted package price</em>, not at a standalone undiscounted price. A
/// refund therefore cannot be derived from a list price at cancellation time; it must read the
/// slice this session was sold for. The allocation is computed once, when the package is bought,
/// and never recomputed — exactly like the booking's price snapshot.
/// </para>
/// <para>
/// <b>Money representation.</b> The amount is an integral count of piastres held as
/// <see cref="long"/> minor units. That is deliberate and not a shortcut: this type lives in the
/// Families domain, which must not reference the Finance module's <c>Money</c> type (module
/// isolation — Finance exclusively owns the persisted money core). Holding
/// <see cref="AllocatedMinorUnits"/> as exact integer minor units keeps the arithmetic exact and
/// lets the Families application layer convert to and from Finance's <c>Money</c> at the seam
/// without ever routing money through a decimal. EGP is the fixed currency of the policy, so no
/// currency column is needed on the allocation itself.
/// </para>
/// <para>
/// <b>The sum invariant.</b> A single allocation cannot know its siblings, so the rule that
/// allocations sum exactly to the paid total is enforced by
/// <see cref="BookingRefundPolicy.AllocatePackageSessions"/>, which is the only supported way to
/// build a package's allocations. The distribution is deterministic and deterministic in one
/// direction only: the first <c>total % count</c> sessions receive one extra piastre, so the
/// remainder is always carried by real sessions and never discarded or invented.
/// </para>
/// </summary>
public sealed class BookingSessionAllocation : ValueObject
{
    /// <summary>Lowest session index that can be allocated. Session numbering is 1-based.</summary>
    public const int FirstSessionIndex = 1;

    private BookingSessionAllocation(
        int sessionIndex,
        long allocatedMinorUnits,
        BookingSessionOutcome outcome,
        DateTime? deliveredOnUtc)
    {
        SessionIndex = sessionIndex;
        AllocatedMinorUnits = allocatedMinorUnits;
        Outcome = outcome;
        DeliveredOnUtc = deliveredOnUtc;
    }

    /// <summary>1-based position of this session inside the package.</summary>
    public int SessionIndex { get; }

    /// <summary>
    /// The exact part of the paid package price allocated to this session, in piastres. Never
    /// negative and never derived from a standalone session price.
    /// </summary>
    public long AllocatedMinorUnits { get; }

    /// <summary>Whether the session was delivered, cancelled, failed or is still scheduled.</summary>
    public BookingSessionOutcome Outcome { get; }

    /// <summary>
    /// UTC instant the session was delivered, recorded only for a
    /// <see cref="BookingSessionOutcome.Delivered"/> session. It is evidence for a later review
    /// (clause 5 complaints are reported within 48 hours of the visit) and never participates in
    /// money arithmetic.
    /// </summary>
    public DateTime? DeliveredOnUtc { get; }

    /// <summary>True only for a session the customer actually received.</summary>
    public bool IsDelivered => Outcome == BookingSessionOutcome.Delivered;

    /// <summary>True while the session has not been delivered and is still refundable on its terms.</summary>
    public bool IsRefundable => Outcome is BookingSessionOutcome.NotDelivered or BookingSessionOutcome.Cancelled;

    /// <summary>
    /// Rehydrates a persisted allocation without re-deriving it. The stored amount is the
    /// authoritative value computed at purchase, so it is validated for shape only (a defined
    /// outcome, a positive session index, a non-negative amount, and an outcome/time pairing that
    /// is not self-contradictory) and never recomputed.
    /// </summary>
    public static BookingSessionAllocation FromPersisted(
        int sessionIndex,
        long allocatedMinorUnits,
        BookingSessionOutcome outcome,
        DateTime? deliveredOnUtc)
    {
        if (sessionIndex < FirstSessionIndex)
            throw new DomainException("A session allocation must have a positive session index.");

        if (allocatedMinorUnits < 0)
            throw new DomainException("A session allocation cannot be a negative amount.");

        if (!Enum.IsDefined(outcome))
            throw new DomainException("A session allocation has an unknown delivery outcome.");

        ValidateDeliveredOnUtc(outcome, deliveredOnUtc);

        return new BookingSessionAllocation(sessionIndex, allocatedMinorUnits, outcome, deliveredOnUtc);
    }

    /// <summary>
    /// Internal construction used by <see cref="BookingRefundPolicy.AllocatePackageSessions"/>,
    /// which owns the sum invariant and the remainder distribution.
    /// </summary>
    internal static BookingSessionAllocation Create(
        int sessionIndex,
        long allocatedMinorUnits,
        BookingSessionOutcome outcome = BookingSessionOutcome.NotDelivered) =>
        FromPersisted(sessionIndex, allocatedMinorUnits, outcome, deliveredOnUtc: null);

    /// <summary>
    /// Returns a copy of this allocation with the session delivered. A delivered session has its
    /// delivery instant recorded and becomes non-refundable; every other field is preserved, so
    /// the allocated amount can never be changed by a delivery marking.
    /// <para>
    /// Saturation is strict in both directions (minor 16): an already delivered session cannot be
    /// delivered again, and a session recorded as provider-failed cannot become delivered — the
    /// two outcomes are contradictory evidence, and silently accepting the second would erase the
    /// first.
    /// </para>
    /// </summary>
    internal BookingSessionAllocation MarkDelivered(DateTime deliveredOnUtc)
    {
        ValidateDeliveredOnUtc(BookingSessionOutcome.Delivered, deliveredOnUtc);

        if (IsDelivered)
            throw new DomainException("This session has already been delivered.");

        if (Outcome == BookingSessionOutcome.ProviderFailed)
            throw new DomainException(
                "A session recorded as provider-failed cannot also be recorded as delivered.");

        return new BookingSessionAllocation(
            SessionIndex,
            AllocatedMinorUnits,
            BookingSessionOutcome.Delivered,
            deliveredOnUtc);
    }

    /// <summary>
    /// Returns a copy of this allocation with the session marked as failed by the provider, which
    /// makes it refundable in full under clause 4. The allocated amount is preserved.
    /// <para>
    /// Saturation is strict: re-marking an already provider-failed session is refused (it would
    /// claim a second failure that never happened), and a delivered session can never become
    /// failed (delivered care is payable). A session that was cancelled or never delivered may
    /// still become provider-failed, because a failure learned after the cancellation is real
    /// evidence and clause 4 refunds that session in full either way.
    /// </para>
    /// </summary>
    internal BookingSessionAllocation MarkProviderFailed()
    {
        if (IsDelivered)
            throw new DomainException("A delivered session cannot also be recorded as provider-failed.");

        if (Outcome == BookingSessionOutcome.ProviderFailed)
            throw new DomainException("This session is already recorded as provider-failed.");

        return new BookingSessionAllocation(
            SessionIndex,
            AllocatedMinorUnits,
            BookingSessionOutcome.ProviderFailed,
            deliveredOnUtc: null);
    }

    private static void ValidateDeliveredOnUtc(BookingSessionOutcome outcome, DateTime? deliveredOnUtc)
    {
        if (outcome != BookingSessionOutcome.Delivered)
        {
            if (deliveredOnUtc is not null)
                throw new DomainException(
                    "Only a delivered session may carry a delivery time.");

            return;
        }

        if (deliveredOnUtc is null || deliveredOnUtc.Value == default)
            throw new DomainException("A delivered session requires the UTC instant it was delivered.");

        if (deliveredOnUtc.Value.Kind != DateTimeKind.Utc)
            throw new DomainException("A session delivery time must be a UTC timestamp.");
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return SessionIndex;
        yield return AllocatedMinorUnits;
        yield return Outcome;
        yield return DeliveredOnUtc;
    }

    public override string ToString() =>
        $"session {SessionIndex}: {AllocatedMinorUnits} piastres ({Outcome})";
}
