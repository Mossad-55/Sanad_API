using Ahlena.BuildingBlocks.Domain.Abstractions;
using Ahlena.BuildingBlocks.Domain.Exceptions;

namespace Ahlena.Modules.Finance.Domain;

public sealed class CaregiverPayoutPolicy : Entity<Guid>
{
    public const int MaximumDescriptionLength = 4000;

    private CaregiverPayoutPolicy() { }

    private CaregiverPayoutPolicy(Guid id, int payoutDelayHours, int version, DateTime effectiveOnUtc, DateTime createdOnUtc, string? descriptionArabic, string? descriptionEnglish) : base(id)
    {
        PayoutDelayHours = payoutDelayHours;
        Version = version;
        EffectiveOnUtc = effectiveOnUtc;
        CreatedOnUtc = createdOnUtc;
        DescriptionArabic = descriptionArabic;
        DescriptionEnglish = descriptionEnglish;
    }

    public int PayoutDelayHours { get; private set; }
    public int Version { get; private set; }
    public DateTime EffectiveOnUtc { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }

    /// <summary>
    /// Admin-authored bilingual payout terms surfaced to caregivers by
    /// GET /api/v1/caregiver/payment-policy. Optional and nullable: policies
    /// created before descriptions existed read back as null (honest nulls).
    /// </summary>
    public string? DescriptionArabic { get; private set; }
    public string? DescriptionEnglish { get; private set; }

    public static CaregiverPayoutPolicy Create(
        int payoutDelayHours,
        int version,
        DateTime effectiveOnUtc,
        DateTime? createdOnUtc = null,
        string? descriptionArabic = null,
        string? descriptionEnglish = null)
    {
        if (payoutDelayHours < 0)
            throw new DomainException("Payout delay must be zero or more hours.");
        if (version <= 0) throw new DomainException("Payout policy version must be positive.");
        if (effectiveOnUtc.Kind != DateTimeKind.Utc) throw new DomainException("Payout policy effective timestamp must be UTC.");
        var created = createdOnUtc ?? DateTime.UtcNow;
        if (created.Kind != DateTimeKind.Utc) throw new DomainException("Payout policy creation timestamp must be UTC.");
        string? normalizedArabic = NormalizeDescription(descriptionArabic, nameof(descriptionArabic));
        string? normalizedEnglish = NormalizeDescription(descriptionEnglish, nameof(descriptionEnglish));
        return new(Guid.CreateVersion7(), payoutDelayHours, version, effectiveOnUtc, created, normalizedArabic, normalizedEnglish);
    }

    private static string? NormalizeDescription(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        string normalized = value.Trim();
        if (normalized.Length > MaximumDescriptionLength)
            throw new DomainException($"Payout policy {field} cannot exceed {MaximumDescriptionLength} characters.");
        return normalized;
    }
}
