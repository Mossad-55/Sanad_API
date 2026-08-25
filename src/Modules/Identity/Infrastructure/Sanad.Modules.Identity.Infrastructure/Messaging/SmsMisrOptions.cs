namespace Sanad.Modules.Identity.Infrastructure.Messaging;

public sealed class SmsMisrOptions
{
    public const string SectionName = "Identity:Sms:SmsMisr";

    public const string OtpSuccessCode = "4901";

    public const string SmsSuccessCode = "1901";

    public string Username { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    public string Sender { get; init; } = string.Empty;

    public string Template { get; init; } = string.Empty;

    public int Environment { get; init; } = 2;

    public string BaseUrl { get; init; } = "https://smsmisr.com";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Username) &&
        !string.IsNullOrWhiteSpace(Password) &&
        !string.IsNullOrWhiteSpace(Sender);

    public bool UseOtpApi =>
        !string.IsNullOrWhiteSpace(Template);
}