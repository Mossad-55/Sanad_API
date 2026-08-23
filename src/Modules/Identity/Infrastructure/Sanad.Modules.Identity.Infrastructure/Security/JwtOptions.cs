namespace Sanad.Modules.Identity.Infrastructure.Security;

public sealed class JwtOptions
{
    public const string SectionName = "Identity:Jwt";

    public string Issuer { get; init; } = string.Empty;

    public string Audience { get; init; } = string.Empty;

    public string SigningKey { get; init; } = string.Empty;
}