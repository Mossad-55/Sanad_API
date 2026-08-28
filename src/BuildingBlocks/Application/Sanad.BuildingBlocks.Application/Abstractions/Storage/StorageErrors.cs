using Sanad.BuildingBlocks.Application.Results;

namespace Sanad.BuildingBlocks.Application.Abstractions.Storage;

public static class StorageErrors
{
    public static readonly Error Empty =
        new("Storage.File.Empty", "File is required.");

    public static readonly Error TooLarge =
        new("Storage.File.TooLarge", "File exceeds the maximum size.");

    public static readonly Error UnsupportedType =
        new("Storage.File.UnsupportedType", "File type is not allowed.");

    public static readonly Error UnsafePath =
        new("Storage.File.UnsafePath", "The file path is not allowed.");
}