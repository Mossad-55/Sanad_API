namespace Ahlena.Modules.Identity.Application.Authentication.Tokens;

public sealed record GeneratedAccessToken(
    string PlainTextToken,
    DateTime ExpiresOnUtc);