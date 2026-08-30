using Sanad.BuildingBlocks.Application.Results;

namespace Sanad.BuildingBlocks.Application.Abstractions.Storage;

public interface IFileStorage
{
    Task<Result<StoredFile>> SaveAsync(
        Stream content,
        string contentType,
        long contentLength,
        string folder,
        CancellationToken cancellationToken = default);

    // For Private Documents Only.
    Task<Result<StoredFile>> SavePrivateAsync(
        Stream content,
        string contentType,
        long contentLength,
        string folder,
        CancellationToken cancellationToken = default);

    Task<Result<PrivateFileContent>> OpenReadAsync(
        string key,
        CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(
        string key,
        CancellationToken cancellationToken = default);
}

public sealed record StoredFile(
    string Key);

public sealed record PrivateFileContent(
    string Key,
    string ContentType,
    Stream Content);