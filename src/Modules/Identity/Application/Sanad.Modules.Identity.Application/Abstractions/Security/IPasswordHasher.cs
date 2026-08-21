namespace Sanad.Modules.Identity.Application.Abstractions.Security;

public interface IPasswordHasher
{
    string Hash(string password);

    PasswordVerificationResult Verify(string passwordHash,
        string providedPassword);
}