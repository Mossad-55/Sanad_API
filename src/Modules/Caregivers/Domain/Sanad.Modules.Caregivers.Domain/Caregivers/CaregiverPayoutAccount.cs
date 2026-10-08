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

    private CaregiverPayoutAccount()
    {
    }

    private CaregiverPayoutAccount(
        CaregiverPayoutAccountId id,
        CaregiverId caregiverId,
        string accountHolderName,
        string bankCode,
        string iban,
        DateTime createdOnUtc)
        : base(id)
    {
        CaregiverId = caregiverId;
        AccountHolderName = accountHolderName;
        BankCode = bankCode;
        Iban = iban;

        Status =
            PayoutAccountStatus.Pending;

        CreatedOnUtc = createdOnUtc;
        UpdatedOnUtc = createdOnUtc;
    }

    public CaregiverId CaregiverId { get; private set; } = default!;

    public string AccountHolderName { get; private set; } = string.Empty;

    public string BankCode { get; private set; } = string.Empty;

    public string Iban { get; private set; } = string.Empty;

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
        string iban)
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

        string normalizedIban =
            NormalizeIban(iban);

        DateTime createdOnUtc = DateTime.UtcNow;

        return new CaregiverPayoutAccount(
            CaregiverPayoutAccountId.New(),
            caregiverId,
            normalizedHolderName,
            normalizedBankCode,
            normalizedIban,
            createdOnUtc);
    }

    public void UpdateDetails(
        string accountHolderName,
        string bankCode,
        string iban)
    {
        AccountHolderName =
            NormalizeHolderName(accountHolderName);

        BankCode =
            NormalizeBankCode(bankCode);

        Iban =
            NormalizeIban(iban);

        Status =
            PayoutAccountStatus.Pending;

        RejectionReason = null;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    public string MaskedIban()
    {
        return string.Concat(
            "****",
            Iban[^4..]);
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

    private static string NormalizeIban(
        string iban)
    {
        if (string.IsNullOrWhiteSpace(iban))
        {
            throw new DomainException(
                "IBAN is required.");
        }

        string normalizedIban =
            iban.Replace(" ", string.Empty)
                .Replace("-", string.Empty)
                .ToUpperInvariant();

        if (normalizedIban.Length > MaximumIbanLength ||
            !IsIbanStructureValid(normalizedIban) ||
            !PassesMod97Check(normalizedIban))
        {
            throw new DomainException(
                "IBAN is invalid.");
        }

        return normalizedIban;
    }

    private static bool IsIbanStructureValid(
        string iban)
    {
        if (iban.Length < 15)
        {
            return false;
        }

        for (int index = 0; index < iban.Length; index++)
        {
            char character = iban[index];

            if (index < 2)
            {
                if (character is < 'A' or > 'Z')
                {
                    return false;
                }
            }
            else if (index < 4)
            {
                if (character is < '0' or > '9')
                {
                    return false;
                }
            }
            else if (character is (< 'A' or > 'Z') and (< '0' or > '9'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool PassesMod97Check(
        string iban)
    {
        string rearranged =
            string.Concat(
                iban.AsSpan(4),
                iban.AsSpan(0, 4));

        int remainder = 0;

        foreach (char character in rearranged)
        {
            int value =
                char.IsDigit(character)
                    ? character - '0'
                    : character - 'A' + 10;

            remainder =
                character is (>= 'A' and <= 'Z')
                    ? (remainder * 100 + value) % 97
                    : (remainder * 10 + value) % 97;
        }

        return remainder == 1;
    }
}

public enum PayoutAccountStatus
{
    Pending = 1,
    Verified = 2,
    Rejected = 3
}
