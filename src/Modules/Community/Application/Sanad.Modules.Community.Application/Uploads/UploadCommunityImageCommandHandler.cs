using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions.Storage;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Community.Application.Abstractions.Data;
using Sanad.Modules.Community.Domain.Uploads;

namespace Sanad.Modules.Community.Application.Uploads;

public sealed class UploadCommunityImageCommandHandler : ICommandHandler<UploadCommunityImageCommand, CommunityImageUploadResponse>
{
    public const string Folder = "community";
    public const long MaximumBytes = 2_097_152;

    private static readonly HashSet<string> AllowedContentTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "image/jpeg",
            "image/png",
            "image/webp"
        };

    private readonly ICommunityDbContext _dbContext;
    private readonly IFileStorage _storage;

    public UploadCommunityImageCommandHandler(
        ICommunityDbContext dbContext,
        IFileStorage storage)
    {
        _dbContext = dbContext;
        _storage = storage;
    }

    public async Task<Result<CommunityImageUploadResponse>> Handle(
        UploadCommunityImageCommand request,
        CancellationToken cancellationToken)
    {
        if (request.UploadedBy == UserId.Empty ||
            request.Length <= 0)
        {
            return Result<CommunityImageUploadResponse>.Failure(
                CommunityImageErrors.Invalid);
        }

        string contentType =
            request.ContentType?.Trim().ToLowerInvariant() ?? string.Empty;

        if (!AllowedContentTypes.Contains(contentType))
        {
            return Result<CommunityImageUploadResponse>.Failure(
                CommunityImageErrors.Invalid);
        }

        byte[] bytes;
        await using (var content = new MemoryStream())
        {
            await request.Content.CopyToAsync(content, cancellationToken);
            bytes = content.ToArray();
        }

        if (bytes.LongLength != request.Length ||
            !HasMatchingSignature(contentType, bytes))
        {
            return Result<CommunityImageUploadResponse>.Failure(
                CommunityImageErrors.Invalid);
        }

        await using var image = new MemoryStream(bytes, writable: false);
        var saved = await _storage.SavePrivateAsync(
            image,
            contentType,
            bytes.LongLength,
            Folder,
            MaximumBytes,
            cancellationToken);

        if (saved.IsFailure)
        {
            return Result<CommunityImageUploadResponse>.Failure(saved.Error);
        }

        try
        {
            CommunityImage record = CommunityImage.Create(
                saved.Value.Key,
                contentType,
                bytes.LongLength,
                request.UploadedBy,
                DateTime.UtcNow);

            _dbContext.CommunityImages.Add(record);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return Result<CommunityImageUploadResponse>.Success(
                new CommunityImageUploadResponse(
                    record.Id.Value,
                    CommunityImageUrls.For(record.Id),
                    contentType,
                    bytes.LongLength));
        }
        catch (DbUpdateException)
        {
            await _storage.DeleteAsync(saved.Value.Key, cancellationToken);
            return Result<CommunityImageUploadResponse>.Failure(
                CommunityImageErrors.Invalid);
        }
        catch
        {
            await _storage.DeleteAsync(saved.Value.Key, cancellationToken);
            throw;
        }
    }

    private static bool HasMatchingSignature(string contentType, byte[] bytes) =>
        contentType switch
        {
            "image/jpeg" =>
                bytes.Length >= 3 &&
                bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff,
            "image/png" =>
                bytes.Length >= 8 &&
                bytes[0] == 137 && bytes[1] == 80 && bytes[2] == 78 && bytes[3] == 71 &&
                bytes[4] == 13 && bytes[5] == 10 && bytes[6] == 26 && bytes[7] == 10,
            "image/webp" =>
                bytes.Length >= 12 &&
                bytes[0] == (byte)'R' && bytes[1] == (byte)'I' &&
                bytes[2] == (byte)'F' && bytes[3] == (byte)'F' &&
                bytes[8] == (byte)'W' && bytes[9] == (byte)'E' &&
                bytes[10] == (byte)'B' && bytes[11] == (byte)'P',
            _ => false
        };
}
