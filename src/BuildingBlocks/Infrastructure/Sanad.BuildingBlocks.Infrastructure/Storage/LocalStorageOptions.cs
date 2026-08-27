namespace Sanad.BuildingBlocks.Infrastructure.Storage;

public sealed class LocalStorageOptions
{
    public const string SectionName = "Storage:Local";

    public const long DefaultMaxBytes = 2_097_152;

    public string RootPath { get; init; } = string.Empty;

    public long MaxBytes { get; init; } = DefaultMaxBytes;

    public string GetEffectiveRootPath()
    {
        if (!string.IsNullOrWhiteSpace(RootPath))
        {
            return RootPath;
        }

        return Path.Combine(
            AppContext.BaseDirectory,
            "sanad-files");
    }
}