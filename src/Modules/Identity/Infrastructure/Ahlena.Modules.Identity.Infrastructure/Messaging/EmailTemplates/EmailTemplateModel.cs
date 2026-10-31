namespace Ahlena.Modules.Identity.Infrastructure.Messaging.EmailTemplates;

/// <summary>
/// Values used by the email templates. Values are treated as plain text and HTML-encoded
/// before they are placed in markup. Required values are specific to each template.
/// </summary>
public sealed class EmailTemplateModel
{
    private readonly IReadOnlyDictionary<string, string> _values;

    public EmailTemplateModel(IReadOnlyDictionary<string, string> values)
        : this(values, isUpcomingSubscriptionRenewal: false, isRetiredPlan: false, null, null)
    {
    }

    private EmailTemplateModel(
        IReadOnlyDictionary<string, string> values,
        bool isUpcomingSubscriptionRenewal,
        bool isRetiredPlan,
        string? otpPurposeAr,
        string? otpPurposeEn)
    {
        ArgumentNullException.ThrowIfNull(values);
        _values = values;
        IsUpcomingSubscriptionRenewal = isUpcomingSubscriptionRenewal;
        IsRetiredPlan = isRetiredPlan;
        OtpPurposeAr = otpPurposeAr;
        OtpPurposeEn = otpPurposeEn;
    }

    /// <summary>Creates data for the upcoming reminder rendered by the SubscriptionRenewed template kind.</summary>
    public static EmailTemplateModel ForUpcomingSubscriptionRenewal(IReadOnlyDictionary<string, string> values) =>
        new(values, isUpcomingSubscriptionRenewal: true, isRetiredPlan: false, null, null);

    /// <summary>Creates data for a retired-plan event rendered by the SubscriptionChanged template kind.</summary>
    public static EmailTemplateModel ForRetiredPlan(IReadOnlyDictionary<string, string> values) =>
        new(values, isUpcomingSubscriptionRenewal: false, isRetiredPlan: true, null, null);

    public static EmailTemplateModel ForOtpCode(IReadOnlyDictionary<string, string> values,
        string purposeAr, string purposeEn) =>
        new(values, isUpcomingSubscriptionRenewal: false, isRetiredPlan: false, purposeAr, purposeEn);

    internal bool IsUpcomingSubscriptionRenewal { get; }
    internal bool IsRetiredPlan { get; }
    internal string? OtpPurposeAr { get; }
    internal string? OtpPurposeEn { get; }

    public string Required(string key)
    {
        if (!_values.TryGetValue(key, out string? value) || string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"Template value '{key}' is required.", nameof(_values));
        }

        return value;
    }
}
