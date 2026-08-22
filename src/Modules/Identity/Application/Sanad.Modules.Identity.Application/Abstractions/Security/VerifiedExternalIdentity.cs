using Sanad.Modules.Identity.Domain.Authentication.ExternalLogins;

namespace Sanad.Modules.Identity.Application.Abstractions.Security;

public sealed record VerifiedExternalIdentity(
    ExternalLoginProvider Provider,
    string ProviderSubject,
    string? VerifiedEmail);