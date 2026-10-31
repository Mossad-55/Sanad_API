namespace Ahlena.API.Options;

/// <summary>
/// DataProtection key-ring configuration (section `DataProtection`).
/// Chat sync cursors are protected with DataProtection, so cursor tokens
/// only survive restarts and redeploys when the key ring is persistent and
/// the application discriminator is stable.
/// </summary>
public sealed class AhlenaDataProtectionOptions
{
    public const string SectionName = "DataProtection";

    public const string DefaultApplicationName = "Ahlena.API";

    /// <summary>
    /// Absolute directory for the persisted key ring (for example
    /// `/var/ahlena/keys` on Linux). Must live outside versioned release
    /// folders so deploys and rollbacks never delete or replace keys.
    /// Empty means the framework default location (Development only).
    /// </summary>
    public string KeyRingPath { get; set; } = string.Empty;

    /// <summary>
    /// Application discriminator isolating this API's protected payloads
    /// from any other application sharing the host.
    /// </summary>
    public string ApplicationName { get; set; } = DefaultApplicationName;

    /// <summary>
    /// Enforces VPS-safe configuration. Outside Development a persistent
    /// absolute key ring is required and fixture/ephemeral keys are never
    /// acceptable on a publicly reachable host.
    /// </summary>
    public void Validate(bool isDevelopment, string contentRootPath)
    {
        if (string.IsNullOrWhiteSpace(ApplicationName))
        {
            throw new InvalidOperationException(
                "DataProtection:ApplicationName must not be empty.");
        }

        if (isDevelopment)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(KeyRingPath))
        {
            throw new InvalidOperationException(
                "DataProtection:KeyRingPath is required outside Development. " +
                "Configure a persistent absolute directory outside the release " +
                "folder (for example /var/ahlena/keys). Ephemeral keys must " +
                "never be used on a publicly reachable host.");
        }

        if (!Path.IsPathFullyQualified(KeyRingPath))
        {
            throw new InvalidOperationException(
                "DataProtection:KeyRingPath must be an absolute path.");
        }

        string fullRoot = NormalizeDirectory(contentRootPath);
        string fullKeys = NormalizeDirectory(KeyRingPath);
        if (fullKeys.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            string.Equals(fullKeys, fullRoot, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "DataProtection:KeyRingPath must live outside the versioned " +
                "release folder so deploys and rollbacks cannot delete keys.");
        }
    }

    private static string NormalizeDirectory(string path) =>
        Path.GetFullPath(path).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
}
