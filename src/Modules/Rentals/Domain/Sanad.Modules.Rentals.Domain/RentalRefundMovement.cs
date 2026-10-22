using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.Modules.Rentals.Domain;

/// <summary>
/// Why a rental refund movement exists. Clause 8 keeps three economically
/// different flows apart, and the contract requires money outcomes to carry
/// distinct, attributable reasons: a cancellation before delivery refunds
/// <em>everything</em> captured, an early return refunds the <em>unused whole
/// prepaid 24-hour periods</em> (Owner decision #19), and the deposit
/// settlement refunds the deposit <em>less documented damage beyond normal
/// wear</em>, netted only with approved on-time deductions (#20, #21).
/// </summary>
public enum RentalRefundMovementKind
{
    /// <summary>Clause 8: "Before delivery: full refund" — the whole captured rent plus deposit.</summary>
    CancellationFullRefund = 1,

    /// <summary>Clause 8 / decision #19: unused whole prepaid periods on early return.</summary>
    EarlyReturnUnusedDays = 2,

    /// <summary>Clause 8: deposit returned within seven business days of receipt and inspection, less documented damage.</summary>
    DepositSettlement = 3
}

/// <summary>
/// The customer's refund destination (Owner decision #10: original payment
/// method by default, app credit only by customer choice, cash via a
/// customer-selected bank transfer, wallet transfer or app credit). The
/// recorded choice is evidence — it never changes the awarded amount.
/// </summary>
public enum RentalRefundDestination
{
    OriginalPaymentMethod = 1,
    AppCredit = 2,
    BankTransfer = 3,
    WalletTransfer = 4
}

/// <summary>
/// One recorded refund movement of a rental booking: the purchasable owner's
/// own row carrying the refund correlation id and outcome (W3 design pin item
/// 3: "... and stores the refund correlation id on its own rows"). The actual
/// money movement is recorded exclusively by the Finance module seams.
/// <para>
/// The <c>(rental_booking_id, kind)</c> unique index is the race boundary: at
/// most one movement per kind per booking can ever exist, so a retried refund
/// either finds the original row (and replays its outcome) or loses the insert
/// to the unique violation and re-reads the winner. There is deliberately no
/// second marker table — the movement row is the idempotency marker.
/// </para>
/// </summary>
public sealed class RentalRefundMovement : Entity<Guid>
{
    public const int MaximumGatewayReferenceLength = 100;
    public const int MaximumEvidenceReferenceLength = 500;

    private RentalRefundMovement() { }

    private RentalRefundMovement(
        Guid id,
        Guid rentalBookingId,
        RentalRefundMovementKind kind,
        Guid refundCaseId,
        RentalRefundDestination destination,
        long amountMinorUnits,
        Guid actorUserId,
        string? gatewayReference,
        string? evidenceReference,
        DateTime recordedOnUtc) : base(id)
    {
        RentalBookingId = rentalBookingId;
        Kind = kind;
        RefundCaseId = refundCaseId;
        Destination = destination;
        AmountMinorUnits = amountMinorUnits;
        ActorUserId = actorUserId;
        GatewayReference = gatewayReference;
        EvidenceReference = evidenceReference;
        RecordedOnUtc = recordedOnUtc;
    }

    public Guid RentalBookingId { get; private set; }

    public RentalRefundMovementKind Kind { get; private set; }

    /// <summary>
    /// The Finance refund-case correlation id. For a gateway/transfer movement
    /// this is the case whose ledger the movement was recorded under; for a
    /// posted app credit it is the booking-chosen case id — the ledger search
    /// key for the movement (the posted entry itself lives under the credit
    /// balance's own case and carries this value in its evidence reference).
    /// </summary>
    public Guid RefundCaseId { get; private set; }

    public RentalRefundDestination Destination { get; private set; }

    /// <summary>Awarded amount, EGP piastres. Zero is a valid recorded award (Owner decision #31).</summary>
    public long AmountMinorUnits { get; private set; }

    public Guid ActorUserId { get; private set; }

    /// <summary>Gateway/transfer reference when the movement completed through the payment rails; null while approval alone stands (#16).</summary>
    public string? GatewayReference { get; private set; }

    public string? EvidenceReference { get; private set; }

    public DateTime RecordedOnUtc { get; private set; }

    /// <summary>
    /// Creates the recorded movement. The amount must be non-negative, the
    /// case and booking ids must be real, and the destination must be a known
    /// value — an undefined enum cast can never become an outcome.
    /// </summary>
    public static RentalRefundMovement Create(
        Guid rentalBookingId,
        RentalRefundMovementKind kind,
        Guid refundCaseId,
        RentalRefundDestination destination,
        long amountMinorUnits,
        Guid actorUserId,
        string? gatewayReference,
        string? evidenceReference,
        DateTime recordedOnUtc)
    {
        if (rentalBookingId == Guid.Empty)
            throw new DomainException("A refund movement requires its booking.");
        if (!Enum.IsDefined(kind))
            throw new DomainException("A refund movement requires a known movement kind.");
        if (refundCaseId == Guid.Empty)
            throw new DomainException("A refund movement requires a Finance refund case id.");
        if (!Enum.IsDefined(destination))
            throw new DomainException("A refund movement requires a known destination.");
        if (amountMinorUnits < 0)
            throw new DomainException("A refund movement award cannot be negative.");
        if (actorUserId == Guid.Empty)
            throw new DomainException("A refund movement requires the acting user.");
        string? gateway = string.IsNullOrWhiteSpace(gatewayReference) ? null : gatewayReference!.Trim();
        if (gateway is { Length: > MaximumGatewayReferenceLength })
            throw new DomainException($"A refund gateway reference must not exceed {MaximumGatewayReferenceLength} characters.");
        string? evidence = string.IsNullOrWhiteSpace(evidenceReference) ? null : evidenceReference!.Trim();
        if (evidence is { Length: > MaximumEvidenceReferenceLength })
            throw new DomainException($"A refund evidence reference must not exceed {MaximumEvidenceReferenceLength} characters.");
        if (recordedOnUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("A refund movement timestamp must be UTC.");

        return new(
            Guid.CreateVersion7(),
            rentalBookingId,
            kind,
            refundCaseId,
            destination,
            amountMinorUnits,
            actorUserId,
            gateway,
            evidence,
            recordedOnUtc);
    }
}
