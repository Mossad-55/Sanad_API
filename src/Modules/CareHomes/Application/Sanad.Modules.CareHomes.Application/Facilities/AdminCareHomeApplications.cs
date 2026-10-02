using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions.Storage;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Application.Abstractions.Data;
using Sanad.Modules.CareHomes.Domain.Facilities;

namespace Sanad.Modules.CareHomes.Application.Facilities;

public sealed record CareHomeAdminApplicationItem(
    Guid Id,
    Guid OwnerUserId,
    CareHomeStatus Status,
    int Version,
    Guid? SubmittedRevisionId,
    string? EnglishName,
    string? ArabicName,
    DateTime UpdatedOnUtc);

public sealed record CareHomeAdminApplicationsPage(
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<CareHomeAdminApplicationItem> Items);

public sealed record CareHomeAdminRevision(
    Guid Id,
    int RevisionNumber,
    bool IsFrozen,
    DateTime CreatedOnUtc,
    DateTime? SubmittedOnUtc,
    DateTime? ApprovedOnUtc,
    CareHomeProfileDraft Profile);

public sealed record CareHomeAdminApplicationDetail(
    Guid Id,
    Guid OwnerUserId,
    CareHomeStatus Status,
    int Version,
    Guid? SubmittedRevisionId,
    Guid? ApprovedRevisionId,
    IReadOnlyList<CareHomeAdminRevision> Revisions,
    IReadOnlyList<CareHomeDocumentResponse> Documents,
    IReadOnlyList<CareHomeReviewHistory> ReviewHistory);

public static class CareHomeAdminErrors
{
    public static readonly Error InvalidQuery = new("CareHomes.Admin.InvalidQuery", "The care-home application query is invalid.");
    public static readonly Error ApplicationNotFound = new("CareHomes.Admin.ApplicationNotFound", "Care-home application was not found.");
    public static readonly Error DocumentNotFound = new("CareHomes.Admin.DocumentNotFound", "Care-home document was not found.");
    public static readonly Error Conflict = new("CareHomes.Admin.Conflict", "The care-home application changed; reload it before reviewing.");
    public static readonly Error InvalidOperation = new("CareHomes.Admin.InvalidOperation", "The care-home review action is not valid in the current state.");
    public static readonly Error PrivateDocumentUnavailable = new("CareHomes.Admin.PrivateDocumentUnavailable", "The private care-home document is unavailable for review.");
}

public sealed record GetAdminCareHomeApplicationsQuery(int Page = 1, int PageSize = 20, CareHomeStatus? Status = null)
    : IQuery<CareHomeAdminApplicationsPage>;

public sealed class GetAdminCareHomeApplicationsQueryHandler(ICareHomesDbContext db)
    : IQueryHandler<GetAdminCareHomeApplicationsQuery, CareHomeAdminApplicationsPage>
{
    public async Task<Result<CareHomeAdminApplicationsPage>> Handle(GetAdminCareHomeApplicationsQuery request, CancellationToken ct)
    {
        if (request.Page < 1 || request.PageSize is < 1 or > 100 ||
            request.Status is not null && !Enum.IsDefined(request.Status.Value))
            return Result<CareHomeAdminApplicationsPage>.Failure(CareHomeAdminErrors.InvalidQuery);

        IQueryable<CareHomeFacility> filtered = db.Facilities.AsNoTracking();
        if (request.Status is not null)
            filtered = filtered.Where(item => item.Status == request.Status.Value);
        int count = await filtered.CountAsync(ct);
        List<CareHomeFacility> facilities = await filtered
            .OrderByDescending(item => item.UpdatedOnUtc)
            .ThenBy(item => item.Id)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Include(item => item.Revisions)
            .AsSplitQuery()
            .ToListAsync(ct);

        CareHomeAdminApplicationItem[] items = facilities.Select(item =>
        {
            CareHomeProfileRevision? revision = item.Revisions.SingleOrDefault(x => x.Id == item.SubmittedRevisionId)
                ?? item.Revisions.OrderByDescending(x => x.RevisionNumber).FirstOrDefault();
            return new CareHomeAdminApplicationItem(item.Id.Value, item.OwnerUserId.Value,
                item.Status, item.Version, item.SubmittedRevisionId, revision?.EnglishName,
                revision?.ArabicName, item.UpdatedOnUtc);
        }).ToArray();

        return new CareHomeAdminApplicationsPage(request.Page, request.PageSize, count, items);
    }
}

public sealed record GetAdminCareHomeApplicationQuery(Guid CareHomeId) : IQuery<CareHomeAdminApplicationDetail>;

public sealed class GetAdminCareHomeApplicationQueryHandler(ICareHomesDbContext db)
    : IQueryHandler<GetAdminCareHomeApplicationQuery, CareHomeAdminApplicationDetail>
{
    public async Task<Result<CareHomeAdminApplicationDetail>> Handle(GetAdminCareHomeApplicationQuery request, CancellationToken ct)
    {
        CareHomeFacility? facility = await db.Facilities.AsNoTracking()
            .Include(x => x.Revisions).Include(x => x.Documents).Include(x => x.ReviewHistory)
            .AsSplitQuery()
            .SingleOrDefaultAsync(x => x.Id == new CareHomeId(request.CareHomeId), ct);
        return facility is null
            ? Result<CareHomeAdminApplicationDetail>.Failure(CareHomeAdminErrors.ApplicationNotFound)
            : Map(facility);
    }

    internal static CareHomeAdminApplicationDetail Map(CareHomeFacility facility) => new(
        facility.Id.Value, facility.OwnerUserId.Value, facility.Status, facility.Version,
        facility.SubmittedRevisionId, facility.ApprovedRevisionId,
        facility.Revisions.OrderBy(x => x.RevisionNumber).Select(revision => new CareHomeAdminRevision(
            revision.Id, revision.RevisionNumber, revision.IsFrozen, revision.CreatedOnUtc,
            revision.SubmittedOnUtc, revision.ApprovedOnUtc,
            new CareHomeProfileDraft(revision.ArabicName, revision.EnglishName,
                revision.ArabicDescription, revision.EnglishDescription, revision.ContactName,
                revision.ContactPhone, revision.ContactEmail, revision.Governorate, revision.City,
                revision.Area, revision.Address, revision.ArabicAdmissionConditions,
                revision.EnglishAdmissionConditions, revision.Amenities, revision.MedicalServices))).ToArray(),
        facility.Documents.OrderByDescending(x => x.CreatedOnUtc)
            .Select(UploadCareHomeDocumentCommandHandler.ToResponse).ToArray(),
        facility.ReviewHistory.OrderBy(x => x.OccurredOnUtc).ToArray());
}

public sealed record ReviewCareHomeDocumentCommand(
    UserId ActorUserId,
    Guid CareHomeId,
    Guid DocumentId,
    int ExpectedVersion,
    bool Verify,
    DateOnly? ExpiryDate,
    bool ConfirmNonExpiring,
    string? Reason) : ICommand<CareHomeAdminApplicationDetail>;

public sealed class ReviewCareHomeDocumentCommandHandler(ICareHomesDbContext db)
    : ICommandHandler<ReviewCareHomeDocumentCommand, CareHomeAdminApplicationDetail>
{
    public async Task<Result<CareHomeAdminApplicationDetail>> Handle(ReviewCareHomeDocumentCommand request, CancellationToken ct)
    {
        CareHomeFacility? facility = await db.Facilities.Include(x => x.Revisions)
            .Include(x => x.Documents).Include(x => x.ReviewHistory).AsSplitQuery()
            .SingleOrDefaultAsync(x => x.Id == new CareHomeId(request.CareHomeId), ct);
        if (facility is null)
            return Result<CareHomeAdminApplicationDetail>.Failure(CareHomeAdminErrors.ApplicationNotFound);
        if (facility.Version != request.ExpectedVersion)
            return Result<CareHomeAdminApplicationDetail>.Failure(CareHomeAdminErrors.Conflict);

        try
        {
            if (request.Verify)
                facility.VerifyDocument(request.ActorUserId, request.ExpectedVersion, request.DocumentId,
                    request.ExpiryDate, request.ConfirmNonExpiring, DateTime.UtcNow);
            else
                facility.RejectDocument(request.ActorUserId, request.ExpectedVersion, request.DocumentId,
                    request.Reason ?? string.Empty, DateTime.UtcNow);
            await db.SaveChangesAsync(ct);
        }
        catch (DomainException)
        {
            return Result<CareHomeAdminApplicationDetail>.Failure(CareHomeAdminErrors.InvalidOperation);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<CareHomeAdminApplicationDetail>.Failure(CareHomeAdminErrors.Conflict);
        }
        return GetAdminCareHomeApplicationQueryHandler.Map(facility);
    }
}

public sealed record ReviewCareHomeApplicationCommand(
    UserId ActorUserId,
    Guid CareHomeId,
    int ExpectedVersion,
    CareHomeReviewAction Action,
    string? Reason) : ICommand<CareHomeAdminApplicationDetail>;

public sealed class ReviewCareHomeApplicationCommandHandler(ICareHomesDbContext db)
    : ICommandHandler<ReviewCareHomeApplicationCommand, CareHomeAdminApplicationDetail>
{
    public async Task<Result<CareHomeAdminApplicationDetail>> Handle(ReviewCareHomeApplicationCommand request, CancellationToken ct)
    {
        CareHomeFacility? facility = await db.Facilities.Include(x => x.Revisions)
            .Include(x => x.Documents).Include(x => x.ReviewHistory).AsSplitQuery()
            .SingleOrDefaultAsync(x => x.Id == new CareHomeId(request.CareHomeId), ct);
        if (facility is null)
            return Result<CareHomeAdminApplicationDetail>.Failure(CareHomeAdminErrors.ApplicationNotFound);
        if (facility.Version != request.ExpectedVersion)
            return Result<CareHomeAdminApplicationDetail>.Failure(CareHomeAdminErrors.Conflict);

        try
        {
            facility.Review(request.ActorUserId, request.ExpectedVersion, request.Action,
                request.Reason, DateTime.UtcNow, DateOnly.FromDateTime(DateTime.UtcNow));
            await db.SaveChangesAsync(ct);
        }
        catch (DomainException)
        {
            return Result<CareHomeAdminApplicationDetail>.Failure(CareHomeAdminErrors.InvalidOperation);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<CareHomeAdminApplicationDetail>.Failure(CareHomeAdminErrors.Conflict);
        }
        return GetAdminCareHomeApplicationQueryHandler.Map(facility);
    }
}

public sealed record GetAdminCareHomeDocumentFileQuery(Guid CareHomeId, Guid DocumentId)
    : IQuery<PrivateFileContent>;

public sealed class GetAdminCareHomeDocumentFileQueryHandler(ICareHomesDbContext db, IFileStorage storage)
    : IQueryHandler<GetAdminCareHomeDocumentFileQuery, PrivateFileContent>
{
    public async Task<Result<PrivateFileContent>> Handle(GetAdminCareHomeDocumentFileQuery request, CancellationToken ct)
    {
        CareHomeFacility? facility = await db.Facilities.AsNoTracking()
            .Include(x => x.Documents)
            .SingleOrDefaultAsync(x => x.Id == new CareHomeId(request.CareHomeId), ct);
        if (facility is null)
            return Result<PrivateFileContent>.Failure(CareHomeAdminErrors.ApplicationNotFound);
        if (facility.Status is not (CareHomeStatus.PendingReview or CareHomeStatus.Approved or CareHomeStatus.Suspended))
            return Result<PrivateFileContent>.Failure(CareHomeAdminErrors.PrivateDocumentUnavailable);
        CareHomeDocument? document = facility.Documents.SingleOrDefault(x => x.Id == request.DocumentId);
        if (document is null)
            return Result<PrivateFileContent>.Failure(CareHomeAdminErrors.DocumentNotFound);
        return await storage.OpenReadAsync(document.PrivateStorageKey, ct);
    }
}
