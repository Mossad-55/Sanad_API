using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.Modules.Rentals.Domain;

/// <summary>
/// One bounded rentable equipment item (refund contract clause 8 and Owner
/// decision #22: "a bounded equipment/rate ... lifecycle. No product-sales or
/// broader inventory platform").
/// <para>
/// The item carries exactly the facts clause 8/#19 require to be displayed
/// before purchase: the daily rate, the deposit and the maximum rental length.
/// There is deliberately no stock/quantity dimension — Rentals is not an
/// inventory platform. Rates and deposits are carried as exact EGP minor
/// units (piastres), the same integer convention the rest of the money core
/// uses for request amounts.
/// </para>
/// <para>
/// Editing an item never changes existing bookings: every booking snapshots
/// the rate, deposit, day cap and display names at booking time (Owner decision
/// #10: catalog changes apply only to future bookings).
/// </para>
/// </summary>
public sealed class RentalEquipment : AggregateRoot<Guid>
{
    public const int MaximumNameLength = 200;
    public const int MaximumDescriptionLength = 2000;
    public const int MaximumRentalDaysLimit = 365;

    private RentalEquipment() { }

    private RentalEquipment(
        Guid id,
        string nameEnglish,
        string nameArabic,
        string? descriptionEnglish,
        string? descriptionArabic,
        long dailyRateMinorUnits,
        long depositMinorUnits,
        int maximumRentalDays,
        DateTime createdOnUtc) : base(id)
    {
        NameEnglish = nameEnglish;
        NameArabic = nameArabic;
        DescriptionEnglish = descriptionEnglish;
        DescriptionArabic = descriptionArabic;
        DailyRateMinorUnits = dailyRateMinorUnits;
        DepositMinorUnits = depositMinorUnits;
        MaximumRentalDays = maximumRentalDays;
        IsActive = true;
        CreatedOnUtc = createdOnUtc;
        UpdatedOnUtc = createdOnUtc;
    }

    public string NameEnglish { get; private set; } = string.Empty;

    public string NameArabic { get; private set; } = string.Empty;

    public string? DescriptionEnglish { get; private set; }

    public string? DescriptionArabic { get; private set; }

    /// <summary>Rent per 24-hour period, EGP piastres. Always strictly positive.</summary>
    public long DailyRateMinorUnits { get; private set; }

    /// <summary>Refundable deposit, EGP piastres. Zero is allowed.</summary>
    public long DepositMinorUnits { get; private set; }

    /// <summary>Longest bookable rental, whole days.</summary>
    public int MaximumRentalDays { get; private set; }

    /// <summary>Inactive items disappear from the catalog and cannot be newly booked; existing bookings keep their snapshots.</summary>
    public bool IsActive { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }

    public DateTime UpdatedOnUtc { get; private set; }

    public static RentalEquipment Create(
        string nameEnglish,
        string nameArabic,
        string? descriptionEnglish,
        string? descriptionArabic,
        long dailyRateMinorUnits,
        long depositMinorUnits,
        int maximumRentalDays,
        DateTime createdOnUtc)
    {
        (string english, string arabic) = NormalizeNames(nameEnglish, nameArabic);
        string? trimmedEnglishDescription = NormalizeDescription(descriptionEnglish, "English");
        string? trimmedArabicDescription = NormalizeDescription(descriptionArabic, "Arabic");
        ValidateTerms(dailyRateMinorUnits, depositMinorUnits, maximumRentalDays);
        ValidateUtc(createdOnUtc, "created");

        return new(
            Guid.CreateVersion7(),
            english,
            arabic,
            trimmedEnglishDescription,
            trimmedArabicDescription,
            dailyRateMinorUnits,
            depositMinorUnits,
            maximumRentalDays,
            createdOnUtc);
    }

    /// <summary>
    /// Catalog edit. Existing bookings are untouched: they carry their own
    /// snapshots taken at booking time.
    /// </summary>
    public void Update(
        string nameEnglish,
        string nameArabic,
        string? descriptionEnglish,
        string? descriptionArabic,
        long dailyRateMinorUnits,
        long depositMinorUnits,
        int maximumRentalDays,
        DateTime updatedOnUtc)
    {
        (string english, string arabic) = NormalizeNames(nameEnglish, nameArabic);
        string? trimmedEnglishDescription = NormalizeDescription(descriptionEnglish, "English");
        string? trimmedArabicDescription = NormalizeDescription(descriptionArabic, "Arabic");
        ValidateTerms(dailyRateMinorUnits, depositMinorUnits, maximumRentalDays);
        ValidateUtc(updatedOnUtc, "updated");

        NameEnglish = english;
        NameArabic = arabic;
        DescriptionEnglish = trimmedEnglishDescription;
        DescriptionArabic = trimmedArabicDescription;
        DailyRateMinorUnits = dailyRateMinorUnits;
        DepositMinorUnits = depositMinorUnits;
        MaximumRentalDays = maximumRentalDays;
        UpdatedOnUtc = updatedOnUtc;
    }

    public void Deactivate(DateTime updatedOnUtc)
    {
        ValidateUtc(updatedOnUtc, "updated");
        IsActive = false;
        UpdatedOnUtc = updatedOnUtc;
    }

    public void Activate(DateTime updatedOnUtc)
    {
        ValidateUtc(updatedOnUtc, "updated");
        IsActive = true;
        UpdatedOnUtc = updatedOnUtc;
    }

    private static (string English, string Arabic) NormalizeNames(string nameEnglish, string nameArabic)
    {
        string english = nameEnglish?.Trim() ?? string.Empty;
        string arabic = nameArabic?.Trim() ?? string.Empty;

        if (english.Length == 0 || english.Length > MaximumNameLength)
            throw new DomainException($"Equipment name (English) must be between 1 and {MaximumNameLength} characters.");
        if (arabic.Length == 0 || arabic.Length > MaximumNameLength)
            throw new DomainException($"Equipment name (Arabic) must be between 1 and {MaximumNameLength} characters.");

        return (english, arabic);
    }

    private static string? NormalizeDescription(string? description, string language)
    {
        if (string.IsNullOrWhiteSpace(description))
            return null;

        string trimmed = description.Trim();
        if (trimmed.Length > MaximumDescriptionLength)
            throw new DomainException($"Equipment description ({language}) must not exceed {MaximumDescriptionLength} characters.");

        return trimmed;
    }

    private static void ValidateTerms(long dailyRateMinorUnits, long depositMinorUnits, int maximumRentalDays)
    {
        if (dailyRateMinorUnits <= 0)
            throw new DomainException("The daily rate must be a positive amount.");
        if (depositMinorUnits < 0)
            throw new DomainException("The deposit cannot be negative.");
        if (maximumRentalDays is < 1 or > MaximumRentalDaysLimit)
            throw new DomainException($"The maximum rental length must be between 1 and {MaximumRentalDaysLimit} days.");
    }

    private static void ValidateUtc(DateTime value, string field)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new DomainException($"The equipment {field} timestamp must be UTC.");
    }
}
