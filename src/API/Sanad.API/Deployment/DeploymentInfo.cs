using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Caregivers.Infrastructure.Persistence;
using Sanad.Modules.CareHomes.Infrastructure.Persistence;
using Sanad.Modules.Chat.Infrastructure.Persistence;
using Sanad.Modules.Cms.Infrastructure.Persistence;
using Sanad.Modules.Community.Infrastructure.Persistence;
using Sanad.Modules.Families.Infrastructure.Persistence;
using Sanad.Modules.Finance.Infrastructure;
using Sanad.Modules.Identity.Infrastructure.Persistence;
using Sanad.Modules.Notifications.Infrastructure.Persistence;

namespace Sanad.API.Deployment;

/// <summary>
/// Safe remote revision/health evidence for release verification.
/// Returns only the package revision, the manifest's required migration
/// identifiers, and the actually applied latest migration per module
/// context. Never exposes connection strings, tokens, or payloads.
/// </summary>
public sealed record DeploymentInfoResponse(
    string Application,
    string Environment,
    string? ManifestRevision,
    IReadOnlyDictionary<string, string> RequiredMigrations,
    IReadOnlyDictionary<string, string?> AppliedMigrations,
    bool DatabaseReachable);

/// <summary>Manifest revision + required migrations read from the published
/// `sanad-deployment-manifest.json` sitting beside the binaries.</summary>
public sealed record DeploymentManifestInfo(
    string? Revision,
    IReadOnlyDictionary<string, string> RequiredMigrations);

public interface IDeploymentManifestReader
{
    DeploymentManifestInfo Read();
}

public sealed class FileDeploymentManifestReader : IDeploymentManifestReader
{
    public DeploymentManifestInfo Read()
    {
        string manifestPath = Path.Combine(
            AppContext.BaseDirectory,
            "sanad-deployment-manifest.json");

        if (!File.Exists(manifestPath))
        {
            return new DeploymentManifestInfo(
                null,
                new Dictionary<string, string>());
        }

        using FileStream stream = File.OpenRead(manifestPath);
        using JsonDocument document = JsonDocument.Parse(stream);

        string? revision = null;
        if (document.RootElement.TryGetProperty("revision", out JsonElement revisionElement) &&
            revisionElement.ValueKind == JsonValueKind.String)
        {
            revision = revisionElement.GetString();
        }

        Dictionary<string, string> required = new(StringComparer.Ordinal);
        if (document.RootElement.TryGetProperty("requiredMigrations", out JsonElement requiredElement) &&
            requiredElement.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in requiredElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    required[property.Name] = property.Value.GetString() ?? string.Empty;
                }
            }
        }

        return new DeploymentManifestInfo(revision, required);
    }
}

public interface IDeploymentMigrationProbe
{
    /// <summary>
    /// Latest applied migration identifier per module context, read from
    /// each context's EF migration history (read-only). No connection
    /// details are ever returned; failures are reported as unreachable
    /// with null entries rather than exception text.
    /// </summary>
    Task<(bool DatabaseReachable, IReadOnlyDictionary<string, string?> Applied)> GetAppliedAsync(
        CancellationToken cancellationToken);
}

public sealed class EfDeploymentMigrationProbe(IServiceProvider serviceProvider) : IDeploymentMigrationProbe
{
    public async Task<(bool DatabaseReachable, IReadOnlyDictionary<string, string?> Applied)> GetAppliedAsync(
        CancellationToken cancellationToken)
    {
        Dictionary<string, string?> applied = new(StringComparer.Ordinal);
        bool reachable = true;

        await ProbeAsync<IdentityDbContext>("Identity", applied, cancellationToken);
        await ProbeAsync<CmsDbContext>("Cms", applied, cancellationToken);
        await ProbeAsync<CaregiversDbContext>("Caregivers", applied, cancellationToken);
        await ProbeAsync<FamiliesDbContext>("Families", applied, cancellationToken);
        await ProbeAsync<CareHomesDbContext>("CareHomes", applied, cancellationToken);
        await ProbeAsync<NotificationsDbContext>("Notifications", applied, cancellationToken);
        await ProbeAsync<CommunityDbContext>("Community", applied, cancellationToken);
        await ProbeAsync<FinanceDbContext>("Finance", applied, cancellationToken);
        await ProbeAsync<ChatDbContext>("Chat", applied, cancellationToken);

        foreach (string? value in applied.Values)
        {
            if (value is null)
            {
                reachable = false;
                break;
            }
        }

        return (reachable, applied);
    }

    private async Task ProbeAsync<TContext>(
        string name,
        Dictionary<string, string?> applied,
        CancellationToken cancellationToken)
        where TContext : DbContext
    {
        try
        {
            using IServiceScope scope = serviceProvider.CreateScope();
            TContext dbContext = scope.ServiceProvider.GetRequiredService<TContext>();
            IEnumerable<string> appliedMigrations =
                await Task.Run(() => dbContext.Database.GetAppliedMigrations(), cancellationToken);
            applied[name] = appliedMigrations.LastOrDefault();
        }
        catch
        {
            // Report only unreachability; exception text may carry host
            // details and must never leave the machine.
            applied[name] = null;
        }
    }
}
