namespace Sanad.Modules.Caregivers.Infrastructure.Security;

public sealed record PayoutIbanProtectionOptions
{
    public const string SectionName = "Caregivers:PayoutIbanProtection";

    public string CurrentKeyId { get; init; } = string.Empty;

    public Dictionary<string, string> Keys { get; init; } = new();

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(CurrentKeyId) &&
        Keys.Count > 0;
}
