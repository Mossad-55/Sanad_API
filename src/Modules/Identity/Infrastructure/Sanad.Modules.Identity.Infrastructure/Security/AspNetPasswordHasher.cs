using Microsoft.AspNetCore.Identity;
using Sanad.Modules.Identity.Application.Abstractions.Security;
using ApplicationPasswordVerificationResult = Sanad.Modules.Identity.Application.Abstractions.Security.PasswordVerificationResult;

namespace Sanad.Modules.Identity.Infrastructure.Security;

public sealed class AspNetPasswordHasher :
    IPasswordHasher
{
    private readonly PasswordHasher<object> _passwordHasher =
        new();

    private static readonly object User =
        new();

    public string Hash(
        string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            password);

        return _passwordHasher.HashPassword(
            User,
            password);
    }

    public ApplicationPasswordVerificationResult Verify(
        string passwordHash,
        string password)
    {
        if (string.IsNullOrWhiteSpace(
                passwordHash) ||
            string.IsNullOrWhiteSpace(
                password))
        {
            return ApplicationPasswordVerificationResult.Failed;
        }

        Microsoft.AspNetCore.Identity
            .PasswordVerificationResult result =
            _passwordHasher.VerifyHashedPassword(
                User,
                passwordHash,
                password);

        return result switch
        {
            Microsoft.AspNetCore.Identity
                .PasswordVerificationResult.Success =>
                    ApplicationPasswordVerificationResult.Success,

            Microsoft.AspNetCore.Identity
                .PasswordVerificationResult
                    .SuccessRehashNeeded =>
                    ApplicationPasswordVerificationResult
                        .SuccessRehashNeeded,

            _ => ApplicationPasswordVerificationResult.Failed
        };
    }
}