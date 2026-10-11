using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Sanad.Modules.Chat.Infrastructure.Persistence;

namespace Sanad.Modules.Chat.Infrastructure;

public sealed class ChatDbContextFactory : IDesignTimeDbContextFactory<ChatDbContext>
{
    public ChatDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable(
                "ConnectionStrings__ChatDatabase")
            ?? Environment.GetEnvironmentVariable(
                "ConnectionStrings__IdentityDatabase");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:ChatDatabase or " +
                "ConnectionStrings:IdentityDatabase is required.");
        }

        var options = new DbContextOptionsBuilder<ChatDbContext>()
            .UseNpgsql(connectionString,
                npgsqlOptions =>
                    npgsqlOptions.MigrationsHistoryTable(
                        "__EFMigrationsHistory",
                        ChatDbContext.Schema))
            .Options;

        return new ChatDbContext(options);
    }
}
