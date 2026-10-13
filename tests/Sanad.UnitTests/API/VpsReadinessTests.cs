using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sanad.API.Controllers;
using Sanad.API.Deployment;
using Sanad.API.Options;

namespace Sanad.UnitTests.API;

/// <summary>
/// Focused VPS-readiness controls: persistent DataProtection key-ring policy,
/// durable storage-root policy, old-cursor recovery across restarts, and the
/// safe deployment-info mapping. No live host, database, or provider is used.
/// </summary>
public sealed class VpsReadinessTests : IDisposable
{
    private readonly string _tempDir;

    public VpsReadinessTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(),
            "sanad-vps-readiness-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void DataProtection_production_requires_persistent_key_ring()
    {
        var options = new SanadDataProtectionOptions();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => options.Validate(isDevelopment: false, contentRootPath: AppContext.BaseDirectory));

        Assert.Contains("KeyRingPath", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DataProtection_production_rejects_relative_key_ring()
    {
        var options = new SanadDataProtectionOptions
        {
            KeyRingPath = Path.Combine("relative", "keys")
        };

        Assert.Throws<InvalidOperationException>(
            () => options.Validate(isDevelopment: false, contentRootPath: AppContext.BaseDirectory));
    }

    [Fact]
    public void DataProtection_production_rejects_release_folder_key_ring()
    {
        var options = new SanadDataProtectionOptions
        {
            KeyRingPath = Path.Combine(AppContext.BaseDirectory, "keys")
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => options.Validate(isDevelopment: false, contentRootPath: AppContext.BaseDirectory));

        Assert.Contains("outside", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DataProtection_production_accepts_absolute_durable_key_ring()
    {
        var options = new SanadDataProtectionOptions
        {
            KeyRingPath = Path.Combine(_tempDir, "keys")
        };

        options.Validate(isDevelopment: false, contentRootPath: AppContext.BaseDirectory);
    }

    [Fact]
    public void DataProtection_development_allows_framework_default_location()
    {
        var options = new SanadDataProtectionOptions();

        options.Validate(isDevelopment: true, contentRootPath: AppContext.BaseDirectory);
    }

    [Fact]
    public void DataProtection_rejects_empty_application_name()
    {
        var options = new SanadDataProtectionOptions
        {
            ApplicationName = string.Empty
        };

        Assert.Throws<InvalidOperationException>(
            () => options.Validate(isDevelopment: true, contentRootPath: AppContext.BaseDirectory));
    }

    [Fact]
    public void Storage_production_rejects_release_folder_root()
    {
        string releaseRoot = Path.Combine(AppContext.BaseDirectory, "sanad-files");

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => StorageRootPolicy.Validate(
                isDevelopment: false,
                effectiveRootPath: releaseRoot,
                appBaseDirectory: AppContext.BaseDirectory));

        Assert.Contains("outside", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Storage_production_accepts_absolute_durable_root()
    {
        string durableRoot = Path.Combine(_tempDir, "files");

        StorageRootPolicy.Validate(
            isDevelopment: false,
            effectiveRootPath: durableRoot,
            appBaseDirectory: AppContext.BaseDirectory);
    }

    [Fact]
    public void Storage_development_keeps_local_default()
    {
        string localDefault = Path.Combine(AppContext.BaseDirectory, "sanad-files");

        StorageRootPolicy.Validate(
            isDevelopment: true,
            effectiveRootPath: localDefault,
            appBaseDirectory: AppContext.BaseDirectory);
    }

    [Fact]
    public void Persistent_keys_recover_old_cursor_after_restart()
    {
        string keyRing = Path.Combine(_tempDir, "keyring");
        Directory.CreateDirectory(keyRing);

        var cursor = new Sanad.Modules.Chat.Application.Sync.ChatSyncCursor(
            Sanad.Modules.Chat.Application.Sync.ChatSyncCursor.CurrentVersion,
            Sanad.Modules.Chat.Application.Sync.ChatSyncCursor.ChangesMode,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Revision: 41,
            Boundary: 41,
            LastSentOnUtc: DateTime.UtcNow,
            LastMessageId: Guid.NewGuid());

        // First boot: protect a cursor with the persistent ring.
        Sanad.API.ChatIntegration.DataProtectionChatSyncCursorProtector firstBoot =
            new(CreatePersistentProvider(keyRing));
        string token = firstBoot.Protect(cursor);

        // Simulated restart: a brand-new provider over the SAME ring and
        // application discriminator must still decrypt the old cursor.
        Sanad.API.ChatIntegration.DataProtectionChatSyncCursorProtector secondBoot =
            new(CreatePersistentProvider(keyRing));

        Assert.True(secondBoot.TryUnprotect(token, out var recovered));
        Assert.NotNull(recovered);
        Assert.Equal(cursor.ConversationId, recovered!.ConversationId);
        Assert.Equal(cursor.RequesterId, recovered.RequesterId);
        Assert.Equal(cursor.Revision, recovered.Revision);
    }

    [Fact]
    public void Ephemeral_keys_do_not_recover_old_cursor()
    {
        var cursor = new Sanad.Modules.Chat.Application.Sync.ChatSyncCursor(
            Sanad.Modules.Chat.Application.Sync.ChatSyncCursor.CurrentVersion,
            Sanad.Modules.Chat.Application.Sync.ChatSyncCursor.ChangesMode,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Revision: 7,
            Boundary: 7,
            LastSentOnUtc: null,
            LastMessageId: null);

        ServiceCollection first = new();
        first.AddDataProtection().UseEphemeralDataProtectionProvider();
        using ServiceProvider firstProvider = first.BuildServiceProvider();
        var firstProtector = new Sanad.API.ChatIntegration.DataProtectionChatSyncCursorProtector(
            firstProvider.GetRequiredService<IDataProtectionProvider>());
        string token = firstProtector.Protect(cursor);

        // A new ephemeral provider (as after any restart) cannot decrypt it.
        ServiceCollection second = new();
        second.AddDataProtection().UseEphemeralDataProtectionProvider();
        using ServiceProvider secondProvider = second.BuildServiceProvider();
        var secondProtector = new Sanad.API.ChatIntegration.DataProtectionChatSyncCursorProtector(
            secondProvider.GetRequiredService<IDataProtectionProvider>());

        Assert.False(secondProtector.TryUnprotect(token, out _));
    }

    [Fact]
    public async Task Deployment_info_returns_revision_and_nine_context_evidence()
    {
        Dictionary<string, string> required = new()
        {
            ["Identity"] = "20261013104759_AddChatMessagesPreference",
            ["Cms"] = "20261012071340_AddLandingSectionHistory",
            ["Caregivers"] = "20261009004058_AddCaregiverPayoutAccountReview",
            ["Families"] = "20261013005844_AddBookingAreaServiceAttribution",
            ["CareHomes"] = "20261012143753_AddCareHomeRoomTypeAmenitiesAndMedia",
            ["Notifications"] = "20261010010618_AddEmailHtmlSnapshots",
            ["Community"] = "20261010004134_AddCommunityCategories",
            ["Finance"] = "20261010143327_AddCaregiverPayoutPolicyDescriptions",
            ["Chat"] = "20261013000659_AddChatPlusChanges"
        };
        Dictionary<string, string?> applied = required.ToDictionary(
            pair => pair.Key,
            pair => (string?)pair.Value);

        var controller = new DeploymentInfoController(
            new FakeManifestReader("abc123", required),
            new FakeProbe(true, applied),
            new TestHostEnvironment(Environments.Staging));

        IActionResult result = await controller.GetDeploymentInfo(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<DeploymentInfoResponse>(ok.Value);
        Assert.Equal("Sanad.API", response.Application);
        Assert.Equal(Environments.Staging, response.Environment);
        Assert.Equal("abc123", response.ManifestRevision);
        Assert.True(response.DatabaseReachable);
        Assert.Equal(9, response.AppliedMigrations.Count);
        foreach (string name in required.Keys)
        {
            Assert.Equal(required[name], response.AppliedMigrations[name]);
        }
    }

    [Fact]
    public async Task Deployment_info_reports_unreachable_database_without_secrets()
    {
        var controller = new DeploymentInfoController(
            new FakeManifestReader("abc123", new Dictionary<string, string>()),
            new FailingProbe(),
            new TestHostEnvironment(Environments.Staging));

        IActionResult result = await controller.GetDeploymentInfo(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<DeploymentInfoResponse>(ok.Value);
        Assert.False(response.DatabaseReachable);
    }

    private static IDataProtectionProvider CreatePersistentProvider(string keyRing)
    {
        ServiceCollection services = new();
        services.AddDataProtection()
            .SetApplicationName(SanadDataProtectionOptions.DefaultApplicationName)
            .PersistKeysToFileSystem(new DirectoryInfo(keyRing));
        ServiceProvider provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IDataProtectionProvider>();
    }

    private sealed class FakeManifestReader(
        string? revision,
        IReadOnlyDictionary<string, string> required) : IDeploymentManifestReader
    {
        public DeploymentManifestInfo Read() => new(revision, required);
    }

    private sealed class FakeProbe(
        bool reachable,
        IReadOnlyDictionary<string, string?> applied) : IDeploymentMigrationProbe
    {
        public Task<(bool DatabaseReachable, IReadOnlyDictionary<string, string?> Applied)> GetAppliedAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult((reachable, applied));
    }

    private sealed class FailingProbe : IDeploymentMigrationProbe
    {
        public Task<(bool DatabaseReachable, IReadOnlyDictionary<string, string?> Applied)> GetAppliedAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<(bool, IReadOnlyDictionary<string, string?>)>(
                (false, new Dictionary<string, string?>()));
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Sanad.UnitTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
