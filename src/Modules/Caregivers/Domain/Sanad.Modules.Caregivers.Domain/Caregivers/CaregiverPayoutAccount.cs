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

    private CaregiverPayoutAccount()
    {
    }

    private CaregiverPayoutAccount(
        CaregiverPayoutAccountId id,
        CaregiverId caregiverId,
        string accountHolderName,
        string bankCode,
        string ibanCiphertext,
        string ibanLast4,
        DateTime createdOnUtc)
        : base(id)
    {
        CaregiverId = caregiverId;
        AccountHolderName = accountHolderName;
        BankCode = bankCode;
        IbanCiphertext = ibanCiphertext;
        IbanLast4 = ibanLast4;

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

    public PayoutAccountStatus Status
    {
        get;
        private set;
    }

    public string? RejectionReason { get; private set; }

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
            caregiverId,
            normalizedHolderName,
            normalizedBankCode,
            normalizedCiphertext,
            normalizedLast4,
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

        RejectionReason = null;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    public string MaskedIban()
    {
        return string.Concat(
            "****",
            IbanLast4);
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
}

public enum PayoutAccountStatus
{
    Pending = 1,
    Verified = 2,
    Rejected = 3
}
