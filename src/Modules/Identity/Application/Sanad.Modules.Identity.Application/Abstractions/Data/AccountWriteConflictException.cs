namespace Sanad.Modules.Identity.Application.Abstractions.Data;

// Infrastructure translates only known account unique constraints, never arbitrary DB failures.
public sealed class AccountWriteConflictException : Exception
{
    public AccountWriteConflictException(bool caregiverExclusivity, Exception innerException)
        : base("An account was added concurrently.", innerException)
    {
        CaregiverExclusivity = caregiverExclusivity;
    }

    public bool CaregiverExclusivity { get; }
}
