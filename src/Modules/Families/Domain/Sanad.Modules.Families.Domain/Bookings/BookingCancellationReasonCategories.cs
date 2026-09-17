using System.Collections.ObjectModel;
using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.Modules.Families.Domain.Bookings;

/// <summary>
/// Closed set of cancellation reason categories plus their Arabic labels.
/// <see cref="BookingCancellationReasonCategory.AccountDeletion"/> is a reason label only: it never
/// triggers, implies or bypasses any account-deletion flow.
/// <para>
/// The exposed collections are read-only views over private backing stores, so no caller can replace
/// or mutate an entry or affect another caller's results.
/// </para>
/// </summary>
public static class BookingCancellationReasonCategories
{
    private static readonly BookingCancellationReasonCategory[] CategoryValues =
    [
        BookingCancellationReasonCategory.Emergency,
        BookingCancellationReasonCategory.MedicalIssues,
        BookingCancellationReasonCategory.TransportationIssues,
        BookingCancellationReasonCategory.AccountDeletion,
        BookingCancellationReasonCategory.Other
    ];

    private static readonly ReadOnlyDictionary<BookingCancellationReasonCategory, string> ArabicLabels =
        new(new Dictionary<BookingCancellationReasonCategory, string>
        {
            [BookingCancellationReasonCategory.Emergency] = "حالة طارئة",
            [BookingCancellationReasonCategory.MedicalIssues] = "أسباب صحية",
            [BookingCancellationReasonCategory.TransportationIssues] = "مشكلات المواصلات",
            [BookingCancellationReasonCategory.AccountDeletion] = "حذف الحساب",
            [BookingCancellationReasonCategory.Other] = "أسباب أخرى"
        });

    /// <summary>All valid categories in presentation order. Read-only; entries cannot be replaced.</summary>
    public static IReadOnlyList<BookingCancellationReasonCategory> All { get; } =
        new ReadOnlyCollection<BookingCancellationReasonCategory>(CategoryValues);

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
