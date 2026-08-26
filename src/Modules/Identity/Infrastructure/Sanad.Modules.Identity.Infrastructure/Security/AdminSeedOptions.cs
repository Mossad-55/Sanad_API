namespace Sanad.Modules.Identity.Infrastructure.Security;

public sealed record AdminSeedOptions
{
    public const string SectionName = "Identity:AdminSeed";

    public string ArabicFullName { get; init; } = string.Empty;

    public string EnglishFullName { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public string PhoneNumber { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ArabicFullName) &&
        !string.IsNullOrWhiteSpace(EnglishFullName) &&
        !string.IsNullOrWhiteSpace(Email) &&
        !string.IsNullOrWhiteSpace(PhoneNumber) &&
        !string.IsNullOrWhiteSpace(Password);
}