using Xunit;

namespace Sanad.UnitTests.Chat;

[AttributeUsage(
    AttributeTargets.Method,
    AllowMultiple = false)]
public sealed class ChatPostgresFactAttribute :
    FactAttribute
{
    public ChatPostgresFactAttribute()
    {
        string? connectionString =
            Environment.GetEnvironmentVariable(
                "ConnectionStrings__IdentityIntegrationDatabase");

        if (string.IsNullOrWhiteSpace(
            connectionString))
        {
            Skip =
                "Chat PostgreSQL race tests are skipped. " +
                "Set ConnectionStrings__IdentityIntegrationDatabase " +
                "to run them.";
        }
    }
}
