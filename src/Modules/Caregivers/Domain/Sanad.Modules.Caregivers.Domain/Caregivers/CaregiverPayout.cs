using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Caregivers.Domain.Caregivers;

public sealed class CaregiverPayout :
    AggregateRoot<CaregiverPayoutId>
{
    public const int MaximumReferenceLength = 200;
    public const int MaximumEvidenceLength = 2000;
    public const int MaximumReasonLength = 2000;

    private CaregiverPayout()
    {
    }

    private CaregiverPayout(
        CaregiverPayoutId id,
        BookingId bookingId,
        CaregiverId caregiverId,
        decimal grossAmount,
        decimal platformFeeAmount,
        decimal netAmount,
        string currency,
        int policyVersion,
        int? chargeRuleVersion,
        string bankCode,
        string maskedIban,
        string transferReference,
        string evidence,
        string reason,
        UserId recordedBy,
        DateTime recordedOnUtc)
        : base(id)
    {
        BookingId = bookingId;
        CaregiverId = caregiverId;
        GrossAmount = grossAmount;
        PlatformFeeAmount = platformFeeAmount;
        NetAmount = netAmount;
        Currency = currency;
        PolicyVersion = policyVersion;
        ChargeRuleVersion = chargeRuleVersion;
        BankCode = bankCode;
        MaskedIban = maskedIban;
        TransferReference = transferReference;
        Evidence = evidence;
        Reason = reason;
        RecordedBy = recordedBy;
        RecordedOnUtc = recordedOnUtc;

        Status =
            PayoutStatus.Paid;
    }

    public BookingId BookingId { get; private set; } = default!;

    public CaregiverId CaregiverId { get; private set; } = default!;

    public decimal GrossAmount { get; private set; }

    public decimal PlatformFeeAmount { get; private set; }

    public decimal NetAmount { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    public int PolicyVersion { get; private set; }

    public int? ChargeRuleVersion { get; private set; }

    public string BankCode { get; private set; } = string.Empty;

    public string MaskedIban { get; private set; } = string.Empty;

    public string TransferReference { get; private set; } = string.Empty;

    public string Evidence { get; private set; } = string.Empty;

    public string Reason { get; private set; } = string.Empty;

    public PayoutStatus Status
    {
        get;
        private set;
    }

    public UserId RecordedBy { get; private set; } = default!;

    public DateTime RecordedOnUtc { get; private set; }

    public string? FailureReason { get; private set; }

    public UserId? FailedBy { get; private set; }

    public DateTime? FailedOnUtc { get; private set; }

    public static CaregiverPayout Record(
        BookingId bookingId,
        CaregiverId caregiverId,
        decimal grossAmount,
        decimal platformFeeAmount,
        string currency,
        int policyVersion,
        int? chargeRuleVersion,
        string bankCode,
        string maskedIban,
        string transferReference,
        string evidence,
        string reason,
        UserId recordedBy,
        DateTime recordedOnUtc)
    {
        if (bookingId == BookingId.Empty)
        {
            throw new DomainException(
                "Booking ID is required.");
        }

        if (caregiverId == CaregiverId.Empty)
        {
            throw new DomainException(
                "Caregiver ID is required.");
        }

        if (recordedBy == UserId.Empty)
        {
            throw new DomainException(
                "Recording actor is required.");
        }

        if (recordedOnUtc.Kind != DateTimeKind.Utc)
        {
            throw new DomainException(
                "Payout timestamps must be UTC.");
        }

        if (grossAmount <= 0m)
        {
            throw new DomainException(
                "Payout gross amount must be greater than zero.");
        }

        if (platformFeeAmount < 0m)
        {
            throw new DomainException(
                "Payout platform fee amount cannot be negative.");
        }

        if (string.IsNullOrWhiteSpace(currency))
        {
            throw new DomainException(
                "Payout currency is required.");
        }

        if (policyVersion <= 0)
        {
            throw new DomainException(
                "Payout policy version must be positive.");
        }

        if (string.IsNullOrWhiteSpace(bankCode))
        {
            throw new DomainException(
                "Payout bank code is required.");
        }

        if (string.IsNullOrWhiteSpace(maskedIban))
        {
            throw new DomainException(
                "Payout masked IBAN is required.");
        }

        string normalizedReference =
            NormalizeText(
                transferReference,
                MaximumReferenceLength,
                "Transfer reference");

        string normalizedEvidence =
            NormalizeText(
                evidence,
                MaximumEvidenceLength,
                "Transfer evidence");

        string normalizedReason =
            NormalizeText(
                reason,
                MaximumReasonLength,
                "Transfer reason");

        // No payout-level fee is deducted: net always equals gross.
        // PlatformFeeAmount is the family-paid snapshot value, kept for
        // audit visibility only and never subtracted.
        return new CaregiverPayout(
            CaregiverPayoutId.New(),
            bookingId,
            caregiverId,
            decimal.Round(grossAmount, 2, MidpointRounding.ToEven),
            decimal.Round(platformFeeAmount, 2, MidpointRounding.ToEven),
            decimal.Round(grossAmount, 2, MidpointRounding.ToEven),
            currency.Trim(),
            policyVersion,
            chargeRuleVersion,
            bankCode.Trim().ToUpperInvariant(),
            maskedIban.Trim(),
            normalizedReference,
            normalizedEvidence,
            normalizedReason,
            recordedBy,
            recordedOnUtc);
    }

    public void MarkFailed(
        string failureReason,
        UserId failedBy,
        DateTime failedOnUtc)
    {
        if (Status != PayoutStatus.Paid)
        {
            throw new DomainException(
                "Only a paid payout can be marked as failed.");
        }

        if (failedBy == UserId.Empty)
        {
            throw new DomainException(
                "Failing actor is required.");
        }

        if (failedOnUtc.Kind != DateTimeKind.Utc)
        {
            throw new DomainException(
                "Payout timestamps must be UTC.");
        }

        FailureReason =
            NormalizeText(
                failureReason,
                MaximumReasonLength,
                "Failure reason");

        FailedBy = failedBy;
        FailedOnUtc = failedOnUtc;
        Status = PayoutStatus.Failed;
    }

    private static string NormalizeText(
        string value,
        int maximumLength,
        string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException(
                $"{field} is required.");
        }

        string normalized = value.Trim();

        if (normalized.Length > maximumLength)
        {
            throw new DomainException(
                $"{field} cannot exceed " +
                $"{maximumLength} characters.");
        }

        return normalized;
    }
}

public enum PayoutStatus
{
    Paid = 1,
    Failed = 2
}
