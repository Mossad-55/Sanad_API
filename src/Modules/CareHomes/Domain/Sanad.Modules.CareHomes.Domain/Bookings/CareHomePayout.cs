using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Domain.Facilities;

namespace Sanad.Modules.CareHomes.Domain.Bookings;

public sealed class CareHomePayout : Entity<Guid>
{
    private CareHomePayout() { }

    private CareHomePayout(Guid id, Guid bookingId, CareHomeId facilityId,
        decimal grossAmount, decimal remainingCustomerAmount, decimal feeRatePercentage,
        decimal feeAmount, decimal netAmount, int chargeRuleVersion,
        string transferReference, string evidence, string reason,
        UserId actor, DateTime recordedOnUtc) : base(id)
    {
        BookingId = bookingId;
        FacilityId = facilityId;
        GrossAmount = grossAmount;
        RemainingCustomerAmount = remainingCustomerAmount;
        FeeRatePercentage = feeRatePercentage;
        FeeAmount = feeAmount;
        NetAmount = netAmount;
        ChargeRuleVersion = chargeRuleVersion;
        Currency = "EGP";
        TransferReference = transferReference;
        Evidence = evidence;
        Reason = reason;
        RecordedBy = actor;
        RecordedOnUtc = recordedOnUtc;
        Version = 1;
    }

    public Guid BookingId { get; private set; }
    public CareHomeId FacilityId { get; private set; }
    public decimal GrossAmount { get; private set; }
    public decimal RemainingCustomerAmount { get; private set; }
    public decimal FeeRatePercentage { get; private set; }
    public decimal FeeAmount { get; private set; }
    public decimal NetAmount { get; private set; }
    public int ChargeRuleVersion { get; private set; }
    public string Currency { get; private set; } = "EGP";
    public string TransferReference { get; private set; } = string.Empty;
    public string Evidence { get; private set; } = string.Empty;
    public string Reason { get; private set; } = string.Empty;
    public UserId RecordedBy { get; private set; }
    public DateTime RecordedOnUtc { get; private set; }
    public int Version { get; private set; }

    public void RecordDebtEntry() => Version++;

    public static CareHomePayout Record(Guid bookingId, CareHomeId facilityId,
        decimal grossAmount, decimal remainingCustomerAmount, decimal feeRatePercentage,
        decimal feeAmount, int chargeRuleVersion, string transferReference,
        string evidence, string reason, UserId actor, DateTime recordedOnUtc)
    {
        if (bookingId == Guid.Empty || facilityId == CareHomeId.Empty || actor == UserId.Empty)
            throw new ArgumentException("A booking, facility, and recording actor are required.");
        if (recordedOnUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("The payout timestamp must be UTC.", nameof(recordedOnUtc));
        if (grossAmount <= 0m || remainingCustomerAmount <= 0m
            || feeRatePercentage is < 0m or > 100m || feeAmount < 0m
            || feeAmount >= grossAmount || chargeRuleVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(grossAmount), "The payout amounts and Finance rule must be valid.");
        decimal gross = decimal.Round(grossAmount, 2, MidpointRounding.ToEven);
        decimal fee = decimal.Round(feeAmount, 2, MidpointRounding.ToEven);
        decimal rate = decimal.Round(feeRatePercentage, 2, MidpointRounding.ToEven);
        if (fee != decimal.Round(gross * rate / 100m, 2, MidpointRounding.ToEven))
            throw new ArgumentException("The payout fee does not match its rate and gross amount.", nameof(feeAmount));
        if (string.IsNullOrWhiteSpace(transferReference) || transferReference.Trim().Length > 200)
            throw new ArgumentException("A transfer reference between 1 and 200 characters is required.", nameof(transferReference));
        if (string.IsNullOrWhiteSpace(evidence) || evidence.Trim().Length > 2000)
            throw new ArgumentException("Transfer evidence between 1 and 2000 characters is required.", nameof(evidence));
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 2000)
            throw new ArgumentException("A reason between 1 and 2000 characters is required.", nameof(reason));

        return new(Guid.CreateVersion7(), bookingId, facilityId, gross,
            decimal.Round(remainingCustomerAmount, 2, MidpointRounding.ToEven), rate,
            fee, decimal.Round(gross - fee, 2, MidpointRounding.ToEven),
            chargeRuleVersion, transferReference.Trim(), evidence.Trim(),
            reason.Trim(), actor, recordedOnUtc);
    }
}

public sealed class CareHomePayoutDebt : Entity<Guid>
{
    private CareHomePayoutDebt() { }

    private CareHomePayoutDebt(Guid id, Guid payoutId, Guid bookingId,
        decimal customerRefundAmount, decimal amount, string reference,
        string reason, UserId actor, DateTime recordedOnUtc) : base(id)
    {
        PayoutId = payoutId;
        BookingId = bookingId;
        CustomerRefundAmount = customerRefundAmount;
        Amount = amount;
        Reference = reference;
        Reason = reason;
        RecordedBy = actor;
        RecordedOnUtc = recordedOnUtc;
    }

    public Guid PayoutId { get; private set; }
    public Guid BookingId { get; private set; }
    public decimal CustomerRefundAmount { get; private set; }
    public decimal Amount { get; private set; }
    public string Reference { get; private set; } = string.Empty;
    public string Reason { get; private set; } = string.Empty;
    public UserId RecordedBy { get; private set; }
    public DateTime RecordedOnUtc { get; private set; }

    public static CareHomePayoutDebt Record(Guid payoutId, Guid bookingId,
        decimal customerRefundAmount, decimal amount, string reference,
        string reason, UserId actor, DateTime recordedOnUtc)
    {
        if (payoutId == Guid.Empty || bookingId == Guid.Empty || actor == UserId.Empty)
            throw new ArgumentException("A payout, booking, and recording actor are required.");
        if (customerRefundAmount <= 0m || amount <= 0m || recordedOnUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Positive refund/debt amounts and a UTC timestamp are required.");
        if (string.IsNullOrWhiteSpace(reference) || reference.Trim().Length > 200)
            throw new ArgumentException("A reference between 1 and 200 characters is required.", nameof(reference));
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 2000)
            throw new ArgumentException("A reason between 1 and 2000 characters is required.", nameof(reason));
        return new(Guid.CreateVersion7(), payoutId, bookingId,
            decimal.Round(customerRefundAmount, 2, MidpointRounding.ToEven),
            decimal.Round(amount, 2, MidpointRounding.ToEven),
            reference.Trim(), reason.Trim(), actor, recordedOnUtc);
    }
}