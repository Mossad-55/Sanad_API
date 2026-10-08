namespace Sanad.Modules.Caregivers.Application.Abstractions.Security;

public interface IIbanProtector
{
    string Protect(
        string plaintextIban);

    string Unprotect(
        string envelope);
}

public sealed class IbanProtectionException : Exception
{
    public IbanProtectionException()
        : base("Payout IBAN protection is unavailable.")
    {
    }
}
