using Ahlena.BuildingBlocks.Domain.Enums;

namespace Ahlena.Modules.Notifications.Application.Notifications;

/// <summary>
/// One localized title/body pair for a generated in-app notification.
/// </summary>
public readonly record struct NotificationCopy(string Title, string Body);

/// <summary>
/// F8 readable-notification copy. One place owns the recipient-facing title and
/// body for the notifications whose copy was previously English-only or
/// repeated raw identifiers, so the stored row is readable in the recipient's
/// own language and never repeats a machine id.
///
/// Rules this catalog keeps:
/// <list type="bullet">
/// <item>Every entry exists in Arabic and English. A recipient whose persisted
/// language is absent or unrecognized keeps the existing Arabic default.</item>
/// <item>No entry can carry an identifier: the conversation/message/ticket/
/// entity ids stay on the structured destination pair (kind + id) and on the
/// idempotency key, which is what routing and deduplication use.</item>
/// <item>Copy that a human authored is never rewritten here. The support-reply
/// body stays exactly what the administrator typed; only its title is chosen
/// from this catalog.</item>
/// </list>
/// </summary>
public static class NotificationTextCatalog
{
    /// <summary>Absent or unrecognized recipient language falls back to Arabic.</summary>
    public const UiLanguage DefaultLanguage = UiLanguage.Arabic;

    /// <summary>
    /// The language of one recipient from a batch lookup. A missing entry (no
    /// persisted language) or a missing lookup is the Arabic default.
    /// </summary>
    public static UiLanguage ResolveLanguage(IReadOnlyDictionary<Guid, UiLanguage>? languages, Guid userId) =>
        languages is not null && languages.TryGetValue(userId, out UiLanguage language)
            ? language
            : DefaultLanguage;

    /// <summary>Arabic is the default, so only an explicit English changes the copy.</summary>
    public static bool IsArabic(UiLanguage language) => language != UiLanguage.English;

    /// <summary>
    /// New-message chat alert. Generic by contract: the conversation and message
    /// ids reach the client through the structured destination and the push
    /// payload, never through this copy.
    /// </summary>
    public static NotificationCopy ChatNewMessage(UiLanguage language) => IsArabic(language)
        ? new NotificationCopy("رسالة دردشة جديدة", "لديك رسالة دردشة جديدة.")
        : new NotificationCopy("New chat message", "You have a new chat message.");

    /// <summary>Negative daily wellness check-in alert.</summary>
    public static NotificationCopy NegativeCheckIn(UiLanguage language) => IsArabic(language)
        ? new NotificationCopy(
            "تنبيه تسجيل الوصول اليومي",
            "أبلغ أحد أفراد الأسرة بأنه لا يشعر بحالة جيدة اليوم.")
        : new NotificationCopy(
            "Daily check-in alert",
            "An elderly family member reported that they are not feeling well today.");

    /// <summary>Help request created by an elderly family member.</summary>
    public static NotificationCopy HelpRequest(UiLanguage language) => IsArabic(language)
        ? new NotificationCopy("طلب مساعدة", "أنشأ أحد أفراد الأسرة طلب مساعدة.")
        : new NotificationCopy("Help request", "An elderly family member created a help request.");

    /// <summary>Scheduled medication dose not taken within the lateness window.</summary>
    public static NotificationCopy MedicationDoseMissed(UiLanguage language) => IsArabic(language)
        ? new NotificationCopy(
            "فوات موعد جرعة الدواء",
            "لم يتم تسجيل تناول جرعة الدواء المجدولة خلال فترة التأخير المسموح بها.")
        : new NotificationCopy(
            "Medication dose missed",
            "A scheduled medication dose has not been taken within the lateness window.");

    /// <summary>SOS alert raised by an elderly family member.</summary>
    public static NotificationCopy SosAlert(UiLanguage language) => IsArabic(language)
        ? new NotificationCopy("تنبيه استغاثة", "أطلق أحد أفراد الأسرة تنبيه استغاثة.")
        : new NotificationCopy("SOS alert", "An elderly family member raised an SOS alert.");

    /// <summary>
    /// Title of the support-reply alert. The body is the administrator-authored
    /// reply and is passed through byte for byte; only this title is localized.
    /// </summary>
    public static string SupportReplyTitle(UiLanguage language) =>
        IsArabic(language) ? "رد الدعم" : "Support reply";

    /// <summary>
    /// Admin-managed account status notice. The status name is passed as a
    /// string because this catalog cannot reference the Identity user-status
    /// enum; an unknown name stays as supplied in both languages.
    /// </summary>
    public static NotificationCopy AdminUserStatus(UiLanguage language, string statusName) => IsArabic(language)
        ? new NotificationCopy("تم تغيير حالة الحساب", $"حالة حسابك الآن {AdminStatusLabelAr(statusName)}.")
        : new NotificationCopy("Account status changed", $"Your account status is now {statusName}.");

    /// <summary>Admin-initiated password reset notice.</summary>
    public static NotificationCopy AdminUserPasswordReset(UiLanguage language) => IsArabic(language)
        ? new NotificationCopy(
            "تمت إعادة تعيين كلمة مرور حسابك",
            "قام أحد المسؤولين بإعادة تعيين كلمة مرور حسابك. تواصل مع الدعم إذا لم تكن تتوقع ذلك.")
        : new NotificationCopy(
            "Your account password was reset",
            "Your account password was reset by an administrator. Contact support if you did not expect this.");

    /// <summary>
    /// Copy this catalog owns for an event that arrives with caller-supplied
    /// copy. The durable elderly check-in alert is the one such event whose
    /// producer lives outside this module, so the handler asks here first and
    /// keeps the supplied title/body only for a pair this catalog does not own.
    /// </summary>
    public static bool TryResolveInboundCopy(
        string category,
        string type,
        UiLanguage language,
        out NotificationCopy copy)
    {
        if (string.Equals(category, "ElderlyCheckIn", StringComparison.Ordinal) &&
            string.Equals(type, "NegativeCheckIn", StringComparison.Ordinal))
        {
            copy = NegativeCheckIn(language);
            return true;
        }

        copy = default;
        return false;
    }

    private static string AdminStatusLabelAr(string statusName) => statusName switch
    {
        "PendingVerification" => "بانتظار التحقق",
        "Active" => "نشطة",
        "Suspended" => "معلّقة",
        "Blocked" => "محظورة",
        _ => statusName
    };
}
