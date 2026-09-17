using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.Modules.Families.Domain.Bookings;

/// <summary>
/// Closed set of cancellation reason categories plus their Arabic labels.
/// <see cref="BookingCancellationReasonCategory.AccountDeletion"/> is a reason label only: it never
/// triggers, implies or bypasses any account-deletion flow.
/// </summary>
public static class BookingCancellationReasonCategories
{
    public static readonly BookingCancellationReasonCategory[] All =
    [
        BookingCancellationReasonCategory.Emergency,
        BookingCancellationReasonCategory.MedicalIssues,
        BookingCancellationReasonCategory.TransportationIssues,
        BookingCancellationReasonCategory.AccountDeletion,
        BookingCancellationReasonCategory.Other
    ];

    private static readonly IReadOnlyDictionary<BookingCancellationReasonCategory, string> ArabicLabels =
        new Dictionary<BookingCancellationReasonCategory, string>
        {
            [BookingCancellationReasonCategory.Emergency] = "حالة طارئة",
            [BookingCancellationReasonCategory.MedicalIssues] = "أسباب صحية",
            [BookingCancellationReasonCategory.TransportationIssues] = "مشكلات المواصلات",
            [BookingCancellationReasonCategory.AccountDeletion] = "حذف الحساب",
            [BookingCancellationReasonCategory.Other] = "أسباب أخرى"
        };

    public static bool TryParse(int value, out BookingCancellationReasonCategory category)
    {
        if (Enum.IsDefined((BookingCancellationReasonCategory)value))
        {
            category = (BookingCancellationReasonCategory)value;
            return true;
        }

        category = default;
        return false;
    }

    public static BookingCancellationReasonCategory Parse(int value)
    {
        if (!TryParse(value, out BookingCancellationReasonCategory category))
            throw new DomainException("Cancellation reason category is unknown.");

        return category;
    }

    public static string GetArabicLabel(BookingCancellationReasonCategory category)
    {
        if (!ArabicLabels.TryGetValue(category, out string? label))
            throw new DomainException("Cancellation reason category is unknown.");

        return label;
    }
}
