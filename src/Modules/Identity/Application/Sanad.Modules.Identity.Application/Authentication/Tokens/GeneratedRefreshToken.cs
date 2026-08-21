namespace Sanad.Modules.Identity.Application.Authentication.Tokens;

public sealed record GeneratedRefreshToken(
    string PlainTextToken,
    string Hash,
    DateTime ExpiresOnUtc);