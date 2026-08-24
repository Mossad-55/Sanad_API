namespace Sanad.Modules.Identity.Application.Abstractions.Security;

public sealed record ExternalIdentityCredential(
    string IdentityToken,
    string Nonce);