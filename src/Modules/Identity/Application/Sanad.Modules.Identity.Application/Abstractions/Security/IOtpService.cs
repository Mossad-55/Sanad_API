namespace Sanad.Modules.Identity.Application.Abstractions.Security;

public interface IOtpService
{
    GeneratedOtpCode Generate(int length);

    bool Verify(string providedCode,
        string otpHash);
}