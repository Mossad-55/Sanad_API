using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions.Storage;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Application.Abstractions.Data;
using Sanad.Modules.CareHomes.Domain.Facilities;

namespace Sanad.Modules.CareHomes.Application.Facilities;

public sealed record CareHomeDocumentResponse(
    Guid Id,
    CareHomeDocumentType Type,
    Guid ProfileRevisionId,
    DateOnly? ExpiryDate,
    bool VerifiedNonExpiring,
    CareHomeDocumentStatus Status,
    string? ReviewReason,
    DateTime CreatedOnUtc);

public sealed record UploadCareHomeDocumentCommand(
    UserId ActorUserId,
    int ExpectedVersion,
    CareHomeDocumentType Type,
    DateOnly? ExpiryDate,
    string ContentType,
    long ContentLength,
    Stream Content) : ICommand<CareHomeDocumentResponse>;

public static class CareHomeDocumentErrors
{
    public static readonly Error NotFound = new("CareHomes.Document.FacilityNotFound", "Care-home facility was not found.");
    public static readonly Error Conflict = new("CareHomes.Document.Conflict", "The care-home application changed; reload it before uploading again.");
    public static readonly Error InvalidContent = new("CareHomes.Document.InvalidContent", "The file content does not match the supported document format.");
}

public sealed class UploadCareHomeDocumentCommandHandler(ICareHomesDbContext db, IFileStorage storage)
    : ICommandHandler<UploadCareHomeDocumentCommand, CareHomeDocumentResponse>
{
    public const long MaximumDocumentBytes = 10_485_760;

    public async Task<Result<CareHomeDocumentResponse>> Handle(UploadCareHomeDocumentCommand request, CancellationToken ct)
    {
        CareHomeFacility? facility = await db.Facilities
            .Include(x => x.Revisions)
            .Include(x => x.Documents)
            .AsSplitQuery()
            .SingleOrDefaultAsync(x => x.OwnerUserId == request.ActorUserId, ct);
        if (facility is null)
            return Result<CareHomeDocumentResponse>.Failure(CareHomeDocumentErrors.NotFound);
        if (facility.Version != request.ExpectedVersion)
            return Result<CareHomeDocumentResponse>.Failure(CareHomeDocumentErrors.Conflict);
        if (!Enum.IsDefined(request.Type) || request.ContentLength is <= 0 or > MaximumDocumentBytes ||
            !MatchesSignature(request.Content, request.ContentType))
            return Result<CareHomeDocumentResponse>.Failure(CareHomeDocumentErrors.InvalidContent);

        Result<StoredFile> saved = await storage.SavePrivateAsync(
            request.Content, request.ContentType, request.ContentLength, "care-home-documents",
            MaximumDocumentBytes, ct);
        if (saved.IsFailure)
            return Result<CareHomeDocumentResponse>.Failure(saved.Error);

        try
        {
            CareHomeDocument document = facility.UploadDocument(
                request.ActorUserId, request.ExpectedVersion, request.Type, saved.Value.Key, request.ContentType,
                request.ContentLength, request.ExpiryDate, DateTime.UtcNow);
            await db.SaveChangesAsync(ct);
            return ToResponse(document);
        }
        catch (DomainException)
        {
            await storage.DeleteAsync(saved.Value.Key, CancellationToken.None);
            return Result<CareHomeDocumentResponse>.Failure(CareHomeDocumentErrors.Conflict);
        }
        catch (DbUpdateConcurrencyException)
        {
            await storage.DeleteAsync(saved.Value.Key, CancellationToken.None);
            return Result<CareHomeDocumentResponse>.Failure(CareHomeDocumentErrors.Conflict);
        }
        catch
        {
            await storage.DeleteAsync(saved.Value.Key, CancellationToken.None);
            throw;
        }
    }

    internal static CareHomeDocumentResponse ToResponse(CareHomeDocument document) => new(
        document.Id, document.Type, document.ProfileRevisionId, document.ExpiryDate,
        document.VerifiedNonExpiring, document.Status, document.ReviewReason, document.CreatedOnUtc);

    private static bool MatchesSignature(Stream content, string contentType)
    {
        if (!content.CanSeek)
            return false;
        long originalPosition = content.Position;
        Span<byte> signature = stackalloc byte[8];
        int count = content.Read(signature);
        content.Position = originalPosition;
        return contentType.ToLowerInvariant() switch
        {
            "application/pdf" => count >= 5 && signature[..5].SequenceEqual("%PDF-"u8),
            "image/jpeg" => count >= 3 && signature[0] == 0xff && signature[1] == 0xd8 && signature[2] == 0xff,
            "image/png" => count >= 8 && signature.SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            _ => false
        };
    }
}
