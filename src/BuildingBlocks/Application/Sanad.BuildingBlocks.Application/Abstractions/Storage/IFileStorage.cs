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
}

public sealed record StoredFile(
    string Key);