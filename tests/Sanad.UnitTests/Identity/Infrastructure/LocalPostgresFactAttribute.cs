using Xunit;

namespace Sanad.UnitTests.Identity.Infrastructure;

[AttributeUsage(
    AttributeTargets.Method,
    AllowMultiple = false)]
public sealed class LocalPostgresFactAttribute :
    FactAttribute
{
    public LocalPostgresFactAttribute()
    {
        string? connectionString =
            Environment.GetEnvironmentVariable(
                "ConnectionStrings__IdentityIntegrationDatabase");

        if (string.IsNullOrWhiteSpace(
            connectionString))
        {
            Skip =
                "Local PostgreSQL integration tests are skipped. " +
                "Set ConnectionStrings__IdentityIntegrationDatabase " +
                "to run them.";
        }
    }
}