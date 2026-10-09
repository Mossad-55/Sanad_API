using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Caregivers.Domain.Caregivers;

public sealed class CaregiverPayoutAccount :
    AggregateRoot<CaregiverPayoutAccountId>
{
    public const int MaximumHolderNameLength = 100;
    public const int MaximumBankCodeLength = 11;
    public const int MaximumIbanLength = 34;
    public const int IbanLast4Length = 4;
    public const int MaximumSourceLength = 200;
    public const int MaximumReferenceLength = 200;
    public const int MaximumReasonLength = 500;

    private CaregiverPayoutAccount()
    {
    }

    private CaregiverPayoutAccount(
        CaregiverPayoutAccountId id,
        string accountHolderName,
        string bankCode,
        string ibanCiphertext,
        string ibanLast4,
        CaregiverId caregiverId,
        DateTime createdOnUtc)
        : base(id)
    {
        CaregiverId = caregiverId;
        AccountHolderName = accountHolderName;
        BankCode = bankCode;
        IbanCiphertext = ibanCiphertext;
        IbanLast4 = ibanLast4;
        Revision = 1;

        Status =
            PayoutAccountStatus.Pending;

        CreatedOnUtc = createdOnUtc;
        UpdatedOnUtc = createdOnUtc;
    }

    public CaregiverId CaregiverId { get; private set; } = default!;

    public string AccountHolderName { get; private set; } = string.Empty;

    public string BankCode { get; private set; } = string.Empty;

    public string IbanCiphertext { get; private set; } = string.Empty;

    public string IbanLast4 { get; private set; } = string.Empty;

    public int Revision { get; private set; }

    public PayoutAccountStatus Status
    {
        get;
        private set;
    }

    public string? RejectionReason { get; private set; }

    public UserId? VerifiedBy { get; private set; }

    public DateTime? VerifiedOnUtc { get; private set; }

    public UserId? ReviewedBy { get; private set; }

    public DateTime? ReviewedOnUtc { get; private set; }

    public string? VerificationSource { get; private set; }

    public string? Reference { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }

    public DateTime UpdatedOnUtc { get; private set; }

    public static CaregiverPayoutAccount Create(
        CaregiverId caregiverId,
        string accountHolderName,
        string bankCode,
        string ibanCiphertext,
        string ibanLast4)
    {
        if (caregiverId == CaregiverId.Empty)
        {
            throw new DomainException(
                "Caregiver ID is required.");
        }

        string normalizedHolderName =
            NormalizeHolderName(accountHolderName);

        string normalizedBankCode =
            NormalizeBankCode(bankCode);

        string normalizedCiphertext =
            NormalizeCiphertext(ibanCiphertext);

        string normalizedLast4 =
            NormalizeLast4(ibanLast4);

        DateTime createdOnUtc = DateTime.UtcNow;

        return new CaregiverPayoutAccount(
            CaregiverPayoutAccountId.New(),
            normalizedHolderName,
            normalizedBankCode,
            normalizedCiphertext,
            normalizedLast4,
            caregiverId,
            createdOnUtc);
    }

    public void UpdateDetails(
        string accountHolderName,
        string bankCode,
        string ibanCiphertext,
        string ibanLast4)
    {
        AccountHolderName =
            NormalizeHolderName(accountHolderName);

        BankCode =
            NormalizeBankCode(bankCode);

        IbanCiphertext =
            NormalizeCiphertext(ibanCiphertext);

        IbanLast4 =
            NormalizeLast4(ibanLast4);

        Status =
            PayoutAccountStatus.Pending;

        Revision = checked(Revision + 1);
        RejectionReason = null;
        VerifiedBy = null;
        VerifiedOnUtc = null;
        ReviewedBy = null;
        ReviewedOnUtc = null;
        VerificationSource = null;
        Reference = null;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void Verify(
        UserId actor,
        int expectedRevision,
        DateTime utcNow,
        string verificationSource,
        string? reference)
    {
        EnsureDecisionPreconditions(actor, expectedRevision, utcNow, PayoutAccountStatus.Pending);

        string normalizedSource = NormalizeSource(verificationSource);

        string? normalizedReference = null;
        if (!string.IsNullOrWhiteSpace(reference))
        {
            normalizedReference = reference.Trim();
            if (normalizedReference.Length > MaximumReferenceLength)
            {
                throw new DomainException(
                    "Verification reference cannot exceed " +
                    $"{MaximumReferenceLength} characters.");
            }
        }

        Status = PayoutAccountStatus.Verified;
        RejectionReason = null;
        VerifiedBy = actor;
        VerifiedOnUtc = utcNow;
        ReviewedBy = actor;
        ReviewedOnUtc = utcNow;
        VerificationSource = normalizedSource;
        Reference = normalizedReference;
        UpdatedOnUtc = utcNow;
    }

    public void Reject(
        UserId actor,
        string reason,
        int expectedRevision,
        DateTime utcNow)
    {
        EnsureDecisionPreconditions(actor, expectedRevision, utcNow, PayoutAccountStatus.Pending);

        Status = PayoutAccountStatus.Rejected;
        RejectionReason = NormalizeReason(reason);
        ReviewedBy = actor;
        ReviewedOnUtc = utcNow;
        UpdatedOnUtc = utcNow;
    }

    public void Revoke(
        UserId actor,
        string reason,
        int expectedRevision,
        DateTime utcNow)
    {
        EnsureDecisionPreconditions(actor, expectedRevision, utcNow, PayoutAccountStatus.Verified);

        Status = PayoutAccountStatus.Revoked;
        RejectionReason = NormalizeReason(reason);
        VerifiedBy = null;
        VerifiedOnUtc = null;
        ReviewedBy = actor;
        ReviewedOnUtc = utcNow;
        UpdatedOnUtc = utcNow;
    }

    public string MaskedIban()
    {
        return string.Concat(
            "****",
            IbanLast4);
    }

    private void EnsureDecisionPreconditions(
        UserId actor,
        int expectedRevision,
        DateTime utcNow,
        PayoutAccountStatus requiredStatus)
    {
        if (actor == UserId.Empty)
        {
            throw new DomainException(
                "Reviewing actor is required.");
        }

        if (utcNow.Kind != DateTimeKind.Utc)
        {
            throw new DomainException(
                "Review timestamps must be UTC.");
        }

        if (expectedRevision != Revision)
        {
            throw new DomainException(
                "The payout account changed since it was reviewed.");
        }

        if (Status != requiredStatus)
        {
            throw new DomainException(
                "The payout account is not in a valid state for this decision.");
        }
    }

    private static string NormalizeHolderName(
        string accountHolderName)
    {
        if (string.IsNullOrWhiteSpace(accountHolderName))
        {
            throw new DomainException(
                "Account holder name is required.");
        }

        string normalizedHolderName =
            accountHolderName.Trim();

        if (normalizedHolderName.Length > MaximumHolderNameLength)
        {
            throw new DomainException(
                "Account holder name cannot exceed " +
                $"{MaximumHolderNameLength} characters.");
        }

        return normalizedHolderName;
    }

    private static string NormalizeBankCode(
        string bankCode)
    {
        if (string.IsNullOrWhiteSpace(bankCode))
        {
            throw new DomainException(
                "Bank code is required.");
        }

        string normalizedBankCode =
            bankCode.Trim().ToUpperInvariant();

        if (normalizedBankCode.Length > MaximumBankCodeLength)
        {
            throw new DomainException(
                "Bank code cannot exceed " +
                $"{MaximumBankCodeLength} characters.");
        }

        return normalizedBankCode;
    }

    private static string NormalizeCiphertext(
        string ibanCiphertext)
    {
        if (string.IsNullOrWhiteSpace(ibanCiphertext))
        {
            throw new DomainException(
                "Protected IBAN payload is required.");
        }

        return ibanCiphertext.Trim();
    }

    private static string NormalizeLast4(
        string ibanLast4)
    {
        if (ibanLast4?.Length != IbanLast4Length)
        {
            throw new DomainException(
                "IBAN display suffix is invalid.");
        }

        return ibanLast4;
    }

    private static string NormalizeSource(
        string verificationSource)
    {
        if (string.IsNullOrWhiteSpace(verificationSource))
        {
            throw new DomainException(
                "Verification source is required.");
        }

        string normalizedSource =
            verificationSource.Trim();

        if (normalizedSource.Length > MaximumSourceLength)
        {
            throw new DomainException(
                "Verification source cannot exceed " +
                $"{MaximumSourceLength} characters.");
        }

        return normalizedSource;
    }

    private static string NormalizeReason(
        string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException(
                "A reason is required.");
        }

        string normalizedReason = reason.Trim();

        if (normalizedReason.Length > MaximumReasonLength)
        {
            throw new DomainException(
                "Reason cannot exceed " +
                $"{MaximumReasonLength} characters.");
        }

        return normalizedReason;
    }
}

public enum PayoutAccountStatus
{
    Pending = 1,
    Verified = 2,
    Rejected = 3,
    Revoked = 4
}
