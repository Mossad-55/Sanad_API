using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.ValueObjects;

namespace Sanad.Modules.Cms.Domain.Help;

/// <summary>
/// One global support contact pair (hotline phone + support email) shown in
/// every app's Help Center. It is not per-role and it never sends an SMS or
/// an email — it is contact-card data only.
/// </summary>
public sealed class SupportContact : Entity<int>
{
    /// <summary>
    /// The fixed key of the single global row. Persistence also enforces
    /// <c>id = 1</c>, so a second row can never exist.
    /// </summary>
    public const int SingletonId = 1;

    public const int MaximumPhoneLength = 16;
    public const int MaximumEmailLength = 254;

    private SupportContact()
    {
    }

    private SupportContact(
        string supportPhone,
        string supportEmail,
        DateTime createdOnUtc)
        : base(SingletonId)
    {
        SupportPhone = supportPhone;
        SupportEmail = supportEmail;
        CreatedOnUtc = createdOnUtc;
        UpdatedOnUtc = createdOnUtc;
    }

    public string SupportPhone { get; private set; } = string.Empty;
    public string SupportEmail { get; private set; } = string.Empty;
    public DateTime CreatedOnUtc { get; private set; }
    public DateTime UpdatedOnUtc { get; private set; }

    public static SupportContact Create(
        string supportPhone,
        string supportEmail)
    {
        return new SupportContact(
            NormalizePhone(supportPhone),
            NormalizeEmail(supportEmail),
            DateTime.UtcNow);
    }

    public void Update(string supportPhone, string supportEmail)
    {
        SupportPhone = NormalizePhone(supportPhone);
        SupportEmail = NormalizeEmail(supportEmail);
        UpdatedOnUtc = DateTime.UtcNow;
    }

    private static string NormalizePhone(string supportPhone)
    {
        if (string.IsNullOrWhiteSpace(supportPhone))
        {
            throw new DomainException(
                "Support phone is required.");
        }

        // Reuse the shared E.164 rule so the hotline is always dialable.
        PhoneNumber phone = PhoneNumber.Create(supportPhone);

        if (phone.Value.Length > MaximumPhoneLength)
        {
            throw new DomainException(
                $"Support phone cannot exceed {MaximumPhoneLength} " +
                "characters.");
        }

        return phone.Value;
    }

    private static string NormalizeEmail(string supportEmail)
    {
        if (string.IsNullOrWhiteSpace(supportEmail))
        {
            throw new DomainException(
                "Support email is required.");
        }

        Email email = Email.Create(supportEmail);

        if (email.Value.Length > MaximumEmailLength)
        {
            throw new DomainException(
                $"Support email cannot exceed {MaximumEmailLength} " +
                "characters.");
        }

        return email.Value;
    }
}
