using Microsoft.EntityFrameworkCore;
using MediatR;
using Sanad.BuildingBlocks.Application.Abstractions.Storage;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Application.Abstractions.Data;
using Sanad.Modules.CareHomes.Domain.Facilities;
using Sanad.Modules.Identity.Application.Users;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.Modules.CareHomes.Application.Facilities;

public sealed record CareHomeAdminApplicationItem(
    Guid Id,
    Guid OwnerUserId,
    string? OwnerArabicName,
    string? OwnerEnglishName,
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

public sealed record CareHomeAdminRevisionMedia(
    Guid Id,
    CareHomeProfileMediaKind Kind,
    int Position,
    string ContentType,
    long Length,
    string Uri);

public sealed record CareHomeAdminRevision(
    Guid Id,
    int RevisionNumber,
    bool IsFrozen,
    DateTime CreatedOnUtc,
    DateTime? SubmittedOnUtc,
    DateTime? ApprovedOnUtc,
    CareHomeProfileDraft Profile,
    IReadOnlyList<CareHomeAdminRevisionMedia> Media);

public sealed record CareHomeAdminReviewHistory(
    Guid Id,
    Guid? ProfileRevisionId,
    CareHomeReviewAction Action,
    CareHomeStatus PreviousStatus,
    CareHomeStatus NewStatus,
    Guid ActorUserId,
    string? Reason,
    DateTime OccurredOnUtc,
    string? ActorArabicName,
    string? ActorEnglishName,
    IReadOnlyList<string>? ActorCurrentRoles);

public sealed record CareHomeAdminApplicationDetail(
    Guid Id,
    Guid OwnerUserId,
    string? OwnerArabicName,
    string? OwnerEnglishName,
    CareHomeStatus Status,
    int Version,
    Guid? SubmittedRevisionId,
    Guid? ApprovedRevisionId,
    IReadOnlyList<CareHomeAdminRevision> Revisions,
    IReadOnlyList<CareHomeDocumentResponse> Documents,
    IReadOnlyList<CareHomeAdminReviewHistory> ReviewHistory);

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

public sealed class GetAdminCareHomeApplicationsQueryHandler(ICareHomesDbContext db, ISender sender)
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

        UserId[] ownerIds = facilities.Select(item => item.OwnerUserId).Distinct().ToArray();
        IReadOnlyList<UserDisplayInfo> ownerInfo = await QueryDisplayInfoAsync(ownerIds, ct);
        Dictionary<Guid, UserDisplayInfo> ownerById = ownerInfo.ToDictionary(x => x.UserId.Value);

        CareHomeAdminApplicationItem[] items = facilities.Select(item =>
        {
            CareHomeProfileRevision? revision = item.Revisions.SingleOrDefault(x => x.Id == item.SubmittedRevisionId)
                ?? item.Revisions.OrderByDescending(x => x.RevisionNumber).FirstOrDefault();
            ownerById.TryGetValue(item.OwnerUserId.Value, out UserDisplayInfo? owner);
            return new CareHomeAdminApplicationItem(item.Id.Value, item.OwnerUserId.Value,
                owner?.ArabicFullName, owner?.EnglishFullName,
                item.Status, item.Version, item.SubmittedRevisionId, revision?.EnglishName,
                revision?.ArabicName, item.UpdatedOnUtc);
        }).ToArray();

        return new CareHomeAdminApplicationsPage(request.Page, request.PageSize, count, items);
    }

    private async Task<IReadOnlyList<UserDisplayInfo>> QueryDisplayInfoAsync(IReadOnlyCollection<UserId> userIds, CancellationToken ct)
    {
        if (userIds.Count == 0) return Array.Empty<UserDisplayInfo>();
        var result = await sender.Send(new GetUserDisplayInfoByIdsQuery(userIds), ct);
        return result.IsSuccess ? result.Value : Array.Empty<UserDisplayInfo>();
    }
}

public sealed record GetAdminCareHomeApplicationQuery(Guid CareHomeId) : IQuery<CareHomeAdminApplicationDetail>;

public sealed class GetAdminCareHomeApplicationQueryHandler(ICareHomesDbContext db, ISender sender)
    : IQueryHandler<GetAdminCareHomeApplicationQuery, CareHomeAdminApplicationDetail>
{
    public async Task<Result<CareHomeAdminApplicationDetail>> Handle(GetAdminCareHomeApplicationQuery request, CancellationToken ct)
    {
        CareHomeFacility? facility = await db.Facilities.AsNoTracking()
            .Include(x => x.Revisions).Include(x => x.Documents).Include(x => x.ReviewHistory)
            .AsSplitQuery()
            .SingleOrDefaultAsync(x => x.Id == new CareHomeId(request.CareHomeId), ct);
        if (facility is null)
            return Result<CareHomeAdminApplicationDetail>.Failure(CareHomeAdminErrors.ApplicationNotFound);

        List<CareHomeProfileMedia> media = await db.ProfileMedia.AsNoTracking()
            .Where(x => facility.Revisions.Select(r => r.Id).Contains(x.ProfileRevisionId))
            .ToListAsync(ct);

        try
        {
            return await Map(facility, media, sender, ct);
        }
        catch (DomainException)
        {
            // A stored profile revision whose structured cancellation terms
            // cannot be re-read fails the admin read closed with a stable
            // code instead of a 500 (out-of-band corruption only; the save
            // and review paths validate the terms themselves).
            return Result<CareHomeAdminApplicationDetail>.Failure(new Error("CareHomes.Bookings.UnreadableSnapshot",
                "A stored profile revision's cancellation terms could not be read; the application record requires correction."));
        }
    }

    internal static async Task<CareHomeAdminApplicationDetail> Map(
        CareHomeFacility facility, IReadOnlyList<CareHomeProfileMedia> media, ISender sender, CancellationToken ct)
    {
        IReadOnlyList<CareHomeProfileMedia> mediaForRevision(Guid revisionId) =>
            media.Where(x => x.ProfileRevisionId == revisionId)
                .OrderBy(x => x.Position).ThenBy(x => x.Id)
                .Select(x => x).ToList();

        IReadOnlyList<CareHomeAdminRevisionMedia> mapMedia(Guid revisionId, IReadOnlyList<CareHomeProfileMedia> items) =>
            items.Select(x => new CareHomeAdminRevisionMedia(x.Id, x.Kind, x.Position,
                x.ContentType, x.Length,
                $"/api/v1/admin/care-homes/{facility.Id.Value:D}/media/{x.Id:D}/file")).ToList();

        UserId[] actorIds = facility.ReviewHistory.Select(x => x.ActorUserId).Distinct().ToArray();
        UserId[] allIds = actorIds.Append(facility.OwnerUserId).Distinct().ToArray();
        IReadOnlyList<UserDisplayInfo> displayInfo = await QueryDisplayInfoAsync(allIds, sender, ct);
        Dictionary<Guid, UserDisplayInfo> infoById = displayInfo.ToDictionary(x => x.UserId.Value);

        infoById.TryGetValue(facility.OwnerUserId.Value, out UserDisplayInfo? owner);

        return new CareHomeAdminApplicationDetail(
            facility.Id.Value, facility.OwnerUserId.Value,
            owner?.ArabicFullName, owner?.EnglishFullName,
            facility.Status, facility.Version,
            facility.SubmittedRevisionId, facility.ApprovedRevisionId,
            facility.Revisions.OrderBy(x => x.RevisionNumber).Select(revision =>
            {
                IReadOnlyList<CareHomeProfileMedia> revisionMedia = mediaForRevision(revision.Id);
                return new CareHomeAdminRevision(
                    revision.Id, revision.RevisionNumber, revision.IsFrozen, revision.CreatedOnUtc,
                    revision.SubmittedOnUtc, revision.ApprovedOnUtc,
                    new CareHomeProfileDraft(revision.ArabicName, revision.EnglishName,
                        revision.ArabicDescription, revision.EnglishDescription, revision.ContactName,
                        revision.ContactPhone, revision.ContactEmail, revision.Governorate, revision.City,
                        revision.Area, revision.Address, revision.ArabicAdmissionConditions,
                        revision.EnglishAdmissionConditions, revision.Amenities, revision.MedicalServices,
                        // Decision #9: structured cancellation terms are part of
                        // the published page, so the reviewer approves them with
                        // the same view the booking flow snapshots.
                        Sanad.Modules.CareHomes.Domain.Bookings.CareHomeCancellationTerms.Parse(
                            revision.CancellationTermsJson)),
                    mapMedia(revision.Id, revisionMedia));
            }).ToArray(),
            facility.Documents.OrderByDescending(x => x.CreatedOnUtc)
                .Select(UploadCareHomeDocumentCommandHandler.ToResponse).ToArray(),
            facility.ReviewHistory.OrderBy(x => x.OccurredOnUtc).Select(history =>
            {
                infoById.TryGetValue(history.ActorUserId.Value, out UserDisplayInfo? actor);
                return new CareHomeAdminReviewHistory(
                    history.Id, history.ProfileRevisionId, history.Action,
                    history.PreviousStatus, history.NewStatus,
                    history.ActorUserId.Value, history.Reason, history.OccurredOnUtc,
                    actor?.ArabicFullName, actor?.EnglishFullName,
                    actor?.AccountTypes.Select(x => ((AccountType)x).ToString()).ToArray());
            }).ToArray());
    }

    private static async Task<IReadOnlyList<UserDisplayInfo>> QueryDisplayInfoAsync(
        IReadOnlyCollection<UserId> userIds, ISender sender, CancellationToken ct)
    {
        if (userIds.Count == 0) return Array.Empty<UserDisplayInfo>();
        var result = await sender.Send(new GetUserDisplayInfoByIdsQuery(userIds), ct);
        return result.IsSuccess ? result.Value : Array.Empty<UserDisplayInfo>();
    }
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

public sealed class ReviewCareHomeDocumentCommandHandler(ICareHomesDbContext db, ISender sender)
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
        List<CareHomeProfileMedia> media = await db.ProfileMedia.AsNoTracking()
            .Where(x => facility.Revisions.Select(r => r.Id).Contains(x.ProfileRevisionId))
            .ToListAsync(ct);
        try
        {
            return await GetAdminCareHomeApplicationQueryHandler.Map(facility, media, sender, ct);
        }
        catch (DomainException)
        {
            return Result<CareHomeAdminApplicationDetail>.Failure(new Error("CareHomes.Bookings.UnreadableSnapshot",
                "A stored profile revision's cancellation terms could not be read; the application record requires correction."));
        }
    }
}

public sealed record ReviewCareHomeApplicationCommand(
    UserId ActorUserId,
    Guid CareHomeId,
    int ExpectedVersion,
    CareHomeReviewAction Action,
    string? Reason) : ICommand<CareHomeAdminApplicationDetail>;

public sealed class ReviewCareHomeApplicationCommandHandler(ICareHomesDbContext db, ISender sender)
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
        List<CareHomeProfileMedia> media = await db.ProfileMedia.AsNoTracking()
            .Where(x => facility.Revisions.Select(r => r.Id).Contains(x.ProfileRevisionId))
            .ToListAsync(ct);
        try
        {
            return await GetAdminCareHomeApplicationQueryHandler.Map(facility, media, sender, ct);
        }
        catch (DomainException)
        {
            return Result<CareHomeAdminApplicationDetail>.Failure(new Error("CareHomes.Bookings.UnreadableSnapshot",
                "A stored profile revision's cancellation terms could not be read; the application record requires correction."));
        }
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
