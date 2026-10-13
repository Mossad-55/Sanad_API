namespace Sanad.API.Options;
/// <summary>
/// Guards the local upload root on service hosts. Release folders are
/// replaced on every deploy and restored on rollback, so public/private
/// uploads must live in a durable directory outside them. Deploy and
/// rollback procedures must never delete or move shared files.
/// </summary>
public static class StorageRootPolicy
{
    /// <summary>
    /// Outside Development the effective storage root must be an absolute
    /// path outside the versioned release folder. Development keeps the
    /// historical `{appBase}/sanad-files` default for local work.
    /// </summary>
    public static void Validate(bool isDevelopment, string effectiveRootPath, string appBaseDirectory)
    {
        if (isDevelopment)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(effectiveRootPath))
        {
            throw new InvalidOperationException(
                "Storage:Local:RootPath is required outside Development. " +
                "Configure a durable absolute directory outside the release " +
                "folder (for example /var/sanad/files).");
        }

        if (!Path.IsPathFullyQualified(effectiveRootPath))
        {
            throw new InvalidOperationException(
                "Storage:Local:RootPath must be an absolute path outside Development.");
        }

        string fullBase = NormalizeDirectory(appBaseDirectory);
        string fullRoot = NormalizeDirectory(effectiveRootPath);
        if (fullRoot.StartsWith(fullBase + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            string.Equals(fullRoot, fullBase, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Storage:Local:RootPath must live outside the versioned release " +
                "folder so deploys and rollbacks cannot delete or move shared files.");
        }
    }

    private static string NormalizeDirectory(string path) =>
        Path.GetFullPath(path).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
}
