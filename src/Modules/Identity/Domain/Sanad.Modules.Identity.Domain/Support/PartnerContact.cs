using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.Modules.Identity.Domain.Support;

/// <summary>
/// A partner/business contact request submitted from the public contact
/// form. The submitter's name, company, email and message are **request
/// data** captured for the support inbox — they never become an account, and
/// no user id is stored: an anonymous submitter has no identity to borrow.
/// <para>
/// The idempotency key and the client key are anti-spam inputs supplied by
/// the caller of the application layer, not contact data.
/// </para>
/// </summary>
public sealed class PartnerContact :
    AggregateRoot<PartnerContactId>
{
    public const int MinimumNameLength = 2;
    public const int MaximumNameLength = 200;
    public const int MaximumCompanyLength = 200;
    public const int MaximumEmailLength = 320;
    public const int MaximumSubjectLength = 150;
    public const int MinimumMessageLength = 10;
    public const int MaximumMessageLength = 2000;
    public const int MaximumIdempotencyKeyLength = 200;
    public const int MaximumClientKeyLength = 128;

    private PartnerContact()
    {
    }

    private PartnerContact(
        PartnerContactId id,
        string name,
        string? company,
        string email,
        string? subject,
        string message,
        string idempotencyKey,
        string clientKey,
        DateTime createdOnUtc)
        : base(id)
    {
        Name = name;
        Company = company;
        Email = email;
        Subject = subject;
        Message = message;
        IdempotencyKey = idempotencyKey;
        ClientKey = clientKey;
        CreatedOnUtc = createdOnUtc;
    }

    /// <summary>The submitter's own name, as submitted.</summary>
    public string Name { get; private set; } = default!;

    /// <summary>The submitter's own company, as submitted (optional).</summary>
    public string? Company { get; private set; }

    /// <summary>The submitter's own email, as submitted.</summary>
    public string Email { get; private set; } = default!;

    /// <summary>The submitted subject (optional).</summary>
    public string? Subject { get; private set; }

    /// <summary>The submitted message.</summary>
    public string Message { get; private set; } = default!;

    /// <summary>The client-supplied idempotency key of this submission.</summary>
    public string IdempotencyKey { get; private set; } = default!;

    /// <summary>
    /// The caller-scoped anti-spam key (the hashed client address). It is
    /// never exposed by any read.
    /// </summary>
    public string ClientKey { get; private set; } = default!;

    public DateTime CreatedOnUtc { get; private set; }

    public static PartnerContact Create(
        string name,
        string? company,
        string email,
        string? subject,
        string message,
        string idempotencyKey,
        string clientKey,
        DateTime createdOnUtc)
    {
        if (createdOnUtc.Kind != DateTimeKind.Utc)
        {
            throw new DomainException(
                "Operation time must be in UTC.");
        }

        string trimmedName = name.Trim();
        if (trimmedName.Length < MinimumNameLength ||
            trimmedName.Length > MaximumNameLength)
        {
            throw new DomainException(
                $"Name must be between {MinimumNameLength} and {MaximumNameLength} characters.");
        }

        string? trimmedCompany = NormalizeOptional(
            company,
            MaximumCompanyLength,
            "Company");

        string trimmedEmail = email.Trim();
        if (trimmedEmail.Length == 0 ||
            trimmedEmail.Length > MaximumEmailLength)
        {
            throw new DomainException(
                $"Email must be between 1 and {MaximumEmailLength} characters.");
        }

        string? trimmedSubject = NormalizeOptional(
            subject,
            MaximumSubjectLength,
            "Subject");

        string trimmedMessage = message.Trim();
        if (trimmedMessage.Length < MinimumMessageLength ||
            trimmedMessage.Length > MaximumMessageLength)
        {
            throw new DomainException(
                $"Message must be between {MinimumMessageLength} and {MaximumMessageLength} characters.");
        }

        string trimmedIdempotencyKey = idempotencyKey.Trim();
        if (trimmedIdempotencyKey.Length == 0 ||
            trimmedIdempotencyKey.Length > MaximumIdempotencyKeyLength)
        {
            throw new DomainException(
                $"Idempotency key must be between 1 and {MaximumIdempotencyKeyLength} characters.");
        }

        string trimmedClientKey = clientKey.Trim();
        if (trimmedClientKey.Length == 0 ||
            trimmedClientKey.Length > MaximumClientKeyLength)
        {
            throw new DomainException(
                $"Client key must be between 1 and {MaximumClientKeyLength} characters.");
        }

        return new PartnerContact(
            PartnerContactId.New(),
            trimmedName,
            trimmedCompany,
            trimmedEmail,
            trimmedSubject,
            trimmedMessage,
            trimmedIdempotencyKey,
            trimmedClientKey,
            createdOnUtc);
    }

    private static string? NormalizeOptional(
        string? value,
        int maximumLength,
        string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new DomainException(
                $"{fieldName} must not exceed {maximumLength} characters.");
        }

        return normalized;
    }
}
