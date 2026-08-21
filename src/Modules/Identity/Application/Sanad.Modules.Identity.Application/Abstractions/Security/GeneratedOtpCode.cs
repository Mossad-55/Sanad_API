namespace Sanad.Modules.Identity.Application.Abstractions.Security;

public sealed record GeneratedOtpCode(
    string PlainTextCode,
    string Hash);