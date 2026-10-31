using System.Net;
using System.Text;

namespace Ahlena.Modules.Identity.Infrastructure.Messaging.EmailTemplates;

/// <summary>Renders the frozen Notifications V1 transactional email template set.</summary>
public static class EmailTemplateCatalog
{
    private const string Brand = "Ahlena";

    private sealed record Copy(string SubjectAr, string SubjectEn, string HeadingAr, string HeadingEn,
        string MessageAr, string MessageEn, string[] Fields);

    private static readonly IReadOnlyDictionary<EmailTemplateKind, Copy> Copies =
        new Dictionary<EmailTemplateKind, Copy>
        {
            [EmailTemplateKind.OtpCode] = new("رمز التحقق من أهلنا", "Your Ahlena verification code", "رمز التحقق", "Verification code", "استخدم الرمز التالي لإكمال التحقق. لا تشاركه مع أي شخص.", "Use the code below to complete verification. Never share it with anyone.", ["code", "expiresIn"]),
            [EmailTemplateKind.Invitation] = new("دعوة للانضمام إلى عائلة في أهلنا", "You're invited to join a family on Ahlena", "لديك دعوة للانضمام", "You have a family invitation", "تمت دعوتك للانضمام إلى العائلة التالية في أهلنا.", "You have been invited to join the following family on Ahlena.", ["familyName", "inviteLink"]),
            [EmailTemplateKind.SupportAcknowledgment] = new("تم استلام طلب الدعم", "We received your support request", "تم استلام طلبك", "Your request has been received", "شكراً لتواصلك مع فريق أهلنا. سنراجع طلبك ونتابع معك.", "Thank you for contacting Ahlena. Our team will review your request and follow up.", ["ticketId", "subject"]),
            [EmailTemplateKind.BookingPaid] = new("تم استلام دفعة الحجز", "Booking payment received", "تم استلام الدفعة", "Booking payment received", "تم استلام دفعة الحجز بنجاح.", "Payment for your booking was received successfully.", ["bookingId", "facilityName", "date"]),
            [EmailTemplateKind.BookingAccepted] = new("تم قبول الحجز", "Your booking was accepted", "تم قبول حجزك", "Your booking was accepted", "تم قبول طلب الحجز.", "Your booking request has been accepted.", ["bookingId", "facilityName", "date"]),
            [EmailTemplateKind.BookingDeclined] = new("تعذر قبول الحجز", "Your booking could not be accepted", "تعذر قبول حجزك", "Your booking could not be accepted", "نأسف، لم يتم قبول طلب الحجز.", "We're sorry, your booking request could not be accepted.", ["bookingId", "facilityName", "date"]),
            [EmailTemplateKind.BookingStarted] = new("بدأت الإقامة", "Your stay has started", "بدأت الإقامة", "Your stay has started", "بدأت الإقامة المرتبطة بحجزك.", "The stay associated with your booking has started.", ["bookingId", "facilityName", "date"]),
            [EmailTemplateKind.BookingCompleted] = new("اكتملت الإقامة", "Your stay is complete", "اكتملت الإقامة", "Your stay is complete", "اكتملت الإقامة المرتبطة بحجزك.", "The stay associated with your booking is complete.", ["bookingId", "facilityName", "date"]),
            [EmailTemplateKind.BookingCancelledWithRefund] = new("إلغاء الحجز واسترداد المبلغ", "Booking cancelled and refund recorded", "تم إلغاء الحجز", "Your booking was cancelled", "تم إلغاء الحجز. تفاصيل استرداد المبلغ موضحة أدناه.", "Your booking was cancelled. The refund details are shown below.", ["bookingId", "facilityName", "refundAmount", "currency"]),
            [EmailTemplateKind.SubscriptionRenewed] = new("تم تجديد الاشتراك", "Your subscription was renewed", "تم تجديد اشتراكك", "Your subscription was renewed", "تم تجديد اشتراكك بنجاح.", "Your subscription was renewed successfully.", ["planName", "amount", "currency", "date"]),
            [EmailTemplateKind.SubscriptionFailed] = new("تعذر تجديد الاشتراك", "Your subscription payment failed", "تعذر إتمام الدفع", "Subscription payment failed", "تعذر إتمام دفعة الاشتراك. يرجى مراجعة وسيلة الدفع.", "We couldn't process your subscription payment. Please review your payment method.", ["planName", "amount", "currency", "date"]),
            [EmailTemplateKind.SubscriptionChanged] = new("تم تحديث الاشتراك", "Your subscription was updated", "تم تحديث اشتراكك", "Your subscription was updated", "تم تغيير تفاصيل خطتك.", "Your plan details have changed.", ["oldPlan", "planName", "date"]),
            [EmailTemplateKind.PayoutRecorded] = new("تم تسجيل دفعة مستحقات", "Payout recorded", "تم تسجيل دفعة المستحقات", "Payout recorded", "تم تسجيل دفعة المستحقات. تجد التفاصيل أدناه.", "A payout has been recorded. See the details below.", ["payoutId", "amount", "currency", "date"]),
            [EmailTemplateKind.PayoutFailed] = new("تعذر تنفيذ دفعة المستحقات", "Payout could not be completed", "تعذر تنفيذ الدفعة", "Payout could not be completed", "تعذر تنفيذ دفعة المستحقات. يرجى مراجعة التفاصيل أو التواصل مع الدعم.", "The payout could not be completed. Review the details or contact support.", ["payoutId", "amount", "currency", "date"]),
            [EmailTemplateKind.PostPublished] = new("تم نشر منشورك", "Your post was published", "تم نشر منشورك", "Your post was published", "أصبح منشورك متاحاً للمجتمع.", "Your post is now available to the community.", ["postTitle"]),
            [EmailTemplateKind.PostRejected] = new("لم تتم الموافقة على منشورك", "Your post was not approved", "لم تتم الموافقة على منشورك", "Your post was not approved", "لم تتم الموافقة على منشورك. يمكنك مراجعة التفاصيل أدناه.", "Your post was not approved. You can review the details below.", ["postTitle", "reason"])
        };

    public static IReadOnlyCollection<EmailTemplateKind> Kinds => Enum.GetValues<EmailTemplateKind>();

    /// <summary>Renders existing event-specific copy through the shared bilingual branded layout.</summary>
    public static RenderedEmailTemplate RenderBilingual(
        string subjectAr, string subjectEn, string headingAr, string headingEn,
        string messageAr, string messageEn)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectAr);
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectEn);
        ArgumentException.ThrowIfNullOrWhiteSpace(headingAr);
        ArgumentException.ThrowIfNullOrWhiteSpace(headingEn);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageAr);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageEn);
        var copy = new Copy(subjectAr, subjectEn, headingAr, headingEn, messageAr, messageEn, []);
        return new RenderedEmailTemplate(
            $"{subjectAr} | {subjectEn}",
            RenderHtml(copy, []),
            RenderPlainText(copy, []));
    }

    public static RenderedEmailTemplate Render(EmailTemplateKind kind, EmailTemplateModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!Copies.TryGetValue(kind, out Copy? copy))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown email template.");
        }

        var fields = copy.Fields.Select(field => (Key: field, Value: model.Required(field))).ToArray();
        if (kind == EmailTemplateKind.OtpCode && model.OtpPurposeAr is not null && model.OtpPurposeEn is not null)
        {
            copy = copy with
            {
                SubjectAr = $"رمز {model.OtpPurposeAr} من أهلنا",
                SubjectEn = $"Your {model.OtpPurposeEn} code",
                HeadingAr = $"رمز {model.OtpPurposeAr}",
                HeadingEn = $"{model.OtpPurposeEn} code",
                MessageAr = "استخدم الرمز التالي لإكمال العملية. لا تشاركه مع أي شخص.",
                MessageEn = "Use the code below to complete this request. Never share it with anyone."
            };
        }
        if (kind == EmailTemplateKind.SubscriptionRenewed && model.IsUpcomingSubscriptionRenewal)
        {
            copy = copy with
            {
                SubjectAr = "تذكير بموعد تجديد الاشتراك",
                SubjectEn = "Your subscription renewal is coming up",
                HeadingAr = "اقترب موعد تجديد اشتراكك",
                HeadingEn = "Your subscription renewal is coming up",
                MessageAr = "سيتم تجديد اشتراكك قريباً. تجد تفاصيل الخطة والموعد والمبلغ أدناه.",
                MessageEn = "Your subscription will renew soon. Review the plan, date, and amount below."
            };
        }
        else if (kind == EmailTemplateKind.SubscriptionChanged && model.IsRetiredPlan)
        {
            copy = copy with
            {
                SubjectAr = "تم إيقاف خطتك",
                SubjectEn = "Your plan has been retired",
                HeadingAr = "تم إيقاف الخطة",
                HeadingEn = "Your plan has been retired",
                MessageAr = "تم إيقاف خطتك السابقة. راجع تفاصيل الخطة البديلة وتاريخ التغيير أدناه.",
                MessageEn = "Your previous plan has been retired. Review the replacement plan and effective date below."
            };
        }
        else if (model.IsUpcomingSubscriptionRenewal || model.IsRetiredPlan)
        {
            throw new ArgumentException(
                "The selected event-specific model can only be used with its matching subscription template kind.",
                nameof(model));
        }

        string subject = $"{copy.SubjectAr} | {copy.SubjectEn}";
        string html = RenderHtml(copy, fields);
        string plainText = RenderPlainText(copy, fields);
        return new RenderedEmailTemplate(subject, html, plainText);
    }

    private static string RenderHtml(Copy copy, (string Key, string Value)[] fields)
    {
        var html = new StringBuilder();
        html.Append("<!doctype html><html lang=\"ar\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>")
            .Append(WebUtility.HtmlEncode(copy.HeadingAr)).Append("</title></head><body style=\"margin:0;padding:24px;background:#f4f6f8;font-family:Arial,Helvetica,sans-serif;color:#17212b\"><main style=\"max-width:600px;margin:0 auto;background:#ffffff;padding:28px;border-radius:12px\"><header style=\"font-size:22px;font-weight:bold;color:#176b62;margin-bottom:24px\">Ahlena</header><section lang=\"ar\" dir=\"rtl\" style=\"text-align:right\"><h1>")
            .Append(WebUtility.HtmlEncode(copy.HeadingAr)).Append("</h1><p>").Append(WebUtility.HtmlEncode(copy.MessageAr)).Append("</p>");
        AppendHtmlFields(html, fields);
        html.Append("</section><hr style=\"border:0;border-top:1px solid #e3e8ec;margin:24px 0\"><section lang=\"en\" dir=\"ltr\" style=\"text-align:left\"><h2>")
            .Append(WebUtility.HtmlEncode(copy.HeadingEn)).Append("</h2><p>").Append(WebUtility.HtmlEncode(copy.MessageEn)).Append("</p>");
        AppendHtmlFields(html, fields);
        html.Append("</section><footer style=\"margin-top:28px;color:#65727e;font-size:13px\">Ahlena | أهلنا</footer></main></body></html>");
        return html.ToString();
    }

    private static void AppendHtmlFields(StringBuilder html, (string Key, string Value)[] fields)
    {
        if (fields.Length == 0) return;
        html.Append("<dl>");
        foreach ((string key, string value) in fields)
        {
            html.Append("<dt style=\"font-weight:bold;margin-top:12px\">").Append(WebUtility.HtmlEncode(Label(key))).Append("</dt><dd style=\"margin:4px 0 0\">");
            html.Append(WebUtility.HtmlEncode(value));
            html.Append("</dd>");
        }
        html.Append("</dl>");
    }

    private static string RenderPlainText(Copy copy, (string Key, string Value)[] fields)
    {
        var text = new StringBuilder()
            .Append("Ahlena | أهلنا").AppendLine().AppendLine()
            .Append(copy.HeadingAr).AppendLine().Append(copy.MessageAr).AppendLine();
        AppendTextFields(text, fields);
        text.AppendLine().Append(copy.HeadingEn).AppendLine().Append(copy.MessageEn).AppendLine();
        AppendTextFields(text, fields);
        return text.ToString().TrimEnd();
    }

    private static void AppendTextFields(StringBuilder text, (string Key, string Value)[] fields)
    {
        foreach ((string key, string value) in fields) text.Append(Label(key)).Append(": ").AppendLine(value);
    }

    private static string Label(string key) => key switch
    {
        "code" => "رمز التحقق / Verification code",
        "expiresIn" => "مدة الصلاحية / Expires in",
        "familyName" => "العائلة / Family",
        "inviteLink" => "رابط الدعوة / Invitation link",
        "ticketId" => "رقم الطلب / Ticket reference",
        "subject" => "الموضوع / Subject",
        "bookingId" => "رقم الحجز / Booking reference",
        "facilityName" => "دار الرعاية / Care home",
        "date" => "التاريخ / Date",
        "refundAmount" => "مبلغ الاسترداد / Refund amount",
        "currency" => "العملة / Currency",
        "planName" => "الخطة / Plan",
        "amount" => "المبلغ / Amount",
        "oldPlan" => "الخطة السابقة / Previous plan",
        "payoutId" => "رقم الدفعة / Payout reference",
        "postTitle" => "عنوان المنشور / Post title",
        "reason" => "التفاصيل / Details",
        _ => key
    };
}
