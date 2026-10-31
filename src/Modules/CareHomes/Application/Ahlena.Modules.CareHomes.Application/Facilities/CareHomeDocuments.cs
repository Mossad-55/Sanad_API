using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.Abstractions.Storage;
using Ahlena.BuildingBlocks.Application.Abstractions;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Exceptions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.CareHomes.Application.Abstractions.Data;
using Ahlena.Modules.CareHomes.Application.Discovery;
using Ahlena.Modules.CareHomes.Domain.Facilities;

namespace Ahlena.Modules.CareHomes.Application.Facilities;

public sealed record CareHomeDocumentResponse(
    Guid Id,
    CareHomeDocumentType Type,
    Guid ProfileRevisionId,
    DateOnly? ExpiryDate,
    bool VerifiedNonExpiring,
    CareHomeDocumentStatus Status,
    string? ReviewReason,
    DateTime CreatedOnUtc,
    // Additive lifecycle grouping (2026-10-27): exactly one of "current",
    // "pending" or "history" — an independent axis over the unchanged
    // Status field, never a stored status. Documented in the consumer guides.
    string LifecycleView,
    // Expired is an honest derived flag over the supplied expiry date
    // (date < Cairo-today, never true when the stored non-expiring flag is
    // set). It overlaps every lifecycle view: a pending candidate with a
    // past date reads expired instead of reassuring, and an expired current
    // document stays current with the flag set.
    bool IsExpired,
    // True only for a pending-view candidate whose stored status is
    // Rejected: the rejected candidate stays visible pending correction
    // instead of disappearing into history.
    bool NeedsAction);

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
    public static readonly Error DocumentNotFound = new("CareHomes.Document.DocumentNotFound", "Care-home document was not found.");
    public static readonly Error NotRemovable = new("CareHomes.Document.NotRemovable", "Only superseded historical care-home documents can be removed; the current effective document and active submitted candidates are protected.");
    public static readonly Error InvalidView = new("CareHomes.Document.InvalidView", "The document view must be one of: all, current, pending, history.");
    public static readonly Error InvalidType = new("CareHomes.Document.InvalidType", "The document type value is not a defined CareHomeDocumentType.");
}

/// <summary>The documented lifecycle-view strings for the additive grouping.</summary>
public static class CareHomeDocumentViews
{
    public const string All = "all";
    public const string Current = "current";
    public const string Pending = "pending";
    public const string History = "history";
}

/// <summary>
/// Deterministic, additive lifecycle classification of a facility's
/// documents (Owner/parent-approved 2026-10-27). Nothing here is stored: the
/// classification is recomputed on every read and at mutation time so a
/// client can never influence it with a supplied flag.
/// <para>
/// Rules (per document type, newest by <c>CreatedOnUtc</c> DESC then
/// <c>Id</c> DESC — the established domain comparator that resolves clone
/// <c>CreatedOnUtc</c> ties):
/// <list type="bullet">
/// <item>current: the newest document of the type inside
/// <c>ApprovedRevisionId</c>. If the newest slot document is Rejected/Revoked
/// there is no current for that type (an older verified document is never
/// resurrected); otherwise it is the newest <b>Verified</b> document — kept
/// even when expired, and never displaced by a newer pending candidate.</item>
/// <item>pending (candidate): the newest document of the type in the target
/// revision — <c>SubmittedRevisionId</c> while the facility is under review,
/// else the unfrozen editable revision — regardless of stored status, so a
/// verified candidate inside an unapproved revision and a rejected candidate
/// awaiting correction both stay visible here. Superseded same-type
/// target-revision documents fall to history.</item>
/// <item>history: everything else, with authored status and expiry
/// verbatim.</item>
/// </list>
/// The approved-revision evaluation always precedes the candidate
/// evaluation, and the target-revision pass never overwrites a
/// current/history verdict of approved-revision rows.
/// <para>
/// Input is the facility's full document set, including logically removed
/// rows (callers keep them out of every serialized view): the effective
/// newest approved-slot state is decided over all rows, so a removed
/// negative latest keeps its "no current" verdict and history deletion can
/// never resurrect an older verified document as current. Pending/history
/// view assignment covers active rows only.
/// </para>
/// </summary>
internal static class CareHomeDocumentLifecycle
{
    public static bool IsExpiredOn(CareHomeDocument document, DateOnly cairoToday) =>
        !document.VerifiedNonExpiring &&
        document.ExpiryDate is DateOnly expiry &&
        expiry < cairoToday;

    public static Guid? ResolveTargetRevisionId(CareHomeFacility facility) =>
        facility.Status == CareHomeStatus.PendingReview && facility.SubmittedRevisionId is Guid submitted
            ? submitted
            : facility.Revisions.LastOrDefault(revision => !revision.IsFrozen)?.Id;

    public static IReadOnlyList<CareHomeDocument> ActiveDocuments(CareHomeFacility facility) =>
        facility.Documents.Where(document => !document.IsRemoved).ToArray();

    public static IReadOnlyDictionary<Guid, string> Classify(
        IReadOnlyCollection<CareHomeDocument> allDocuments,
        Guid? approvedRevisionId,
        Guid? targetRevisionId)
    {
        var views = new Dictionary<Guid, string>();
        foreach (var group in allDocuments.GroupBy(document => document.Type))
        {
            var ordered = group.OrderByDescending(document => document.CreatedOnUtc)
                .ThenByDescending(document => document.Id)
                .ToArray();

            if (approvedRevisionId is Guid approved)
            {
                // Effective state is computed over ALL rows of the approved
                // slot, including logically removed ones: a removed
                // rejected/revoked latest keeps its "no current" verdict, so
                // deleting it from history can never resurrect an older
                // verified document as current.
                var approvedSlotAll = ordered.Where(document =>
                    document.ProfileRevisionId == approved).ToArray();
                if (approvedSlotAll.Length > 0)
                {
                    bool effectiveNegative = approvedSlotAll[0].Status
                        is CareHomeDocumentStatus.Rejected or CareHomeDocumentStatus.Revoked;
                    var approvedSlotActive = approvedSlotAll.Where(document =>
                        !document.IsRemoved).ToArray();

                    if (!effectiveNegative)
                    {
                        var verified = approvedSlotActive.FirstOrDefault(document =>
                            document.Status == CareHomeDocumentStatus.Verified);
                        if (verified is not null)
                            views[verified.Id] = CareHomeDocumentViews.Current;
                    }

                    foreach (var document in approvedSlotActive.Where(document => !views.ContainsKey(document.Id)))
                        views[document.Id] = CareHomeDocumentViews.History;
                }
            }

            var activeOrdered = ordered.Where(document => !document.IsRemoved).ToArray();

            if (targetRevisionId is Guid target && target != approvedRevisionId)
            {
                var targetSlot = activeOrdered.Where(document => document.ProfileRevisionId == target).ToArray();
                if (targetSlot.Length > 0)
                {
                    views[targetSlot[0].Id] = CareHomeDocumentViews.Pending;
                    foreach (var document in targetSlot.Skip(1))
                        views[document.Id] = CareHomeDocumentViews.History;
                }
            }

            foreach (var document in activeOrdered.Where(document => !views.ContainsKey(document.Id)))
                views[document.Id] = CareHomeDocumentViews.History;
        }

        return views;
    }

    public static string ViewOf(IReadOnlyDictionary<Guid, string> views, Guid documentId) =>
        views.TryGetValue(documentId, out string? view) ? view : CareHomeDocumentViews.History;

    public static bool NeedsActionOf(string view, CareHomeDocument document) =>
        view == CareHomeDocumentViews.Pending && document.Status == CareHomeDocumentStatus.Rejected;

    internal static CareHomeDocumentResponse ToResponse(
        CareHomeDocument document,
        IReadOnlyDictionary<Guid, string> views,
        DateOnly cairoToday)
    {
        string view = ViewOf(views, document.Id);
        return new(
            document.Id, document.Type, document.ProfileRevisionId, document.ExpiryDate,
            document.VerifiedNonExpiring, document.Status, document.ReviewReason, document.CreatedOnUtc,
            view, IsExpiredOn(document, cairoToday), NeedsActionOf(view, document));
    }
}

public sealed class UploadCareHomeDocumentCommandHandler(ICareHomesDbContext db, IFileStorage storage, IDateTimeProvider clock)
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
                request.ContentLength, request.ExpiryDate, clock.UtcNow);
            await db.SaveChangesAsync(ct);
            // A fresh upload is always the newest candidate of the editable
            // target revision (pending view); the shared projection computes
            // exactly that with the facility's real revision pointers.
            IReadOnlyDictionary<Guid, string> views = CareHomeDocumentLifecycle.Classify(
                facility.Documents,
                facility.ApprovedRevisionId,
                CareHomeDocumentLifecycle.ResolveTargetRevisionId(facility));
            return CareHomeDocumentLifecycle.ToResponse(document, views,
                CareHomePublicEligibility.CairoDate(clock.UtcNow));
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

/// <summary>
/// Additive document-list readback for the new owner/admin tabs: labeled
/// items from one deterministic projection, optional filters, validated
/// inputs (unknown values are coded 400s, never a silent empty result).
/// </summary>
public sealed record CareHomeDocumentListResponse(
    string View,
    CareHomeDocumentType? Type,
    bool? IsExpired,
    int TotalCount,
    IReadOnlyList<CareHomeDocumentResponse> Items);

public sealed record GetMyCareHomeDocumentsQuery(
    UserId ActorUserId,
    string? View,
    CareHomeDocumentType? Type,
    bool? IsExpired) : IQuery<CareHomeDocumentListResponse>;

public sealed record GetAdminCareHomeDocumentsQuery(
    Guid CareHomeId,
    string? View,
    CareHomeDocumentType? Type,
    bool? IsExpired) : IQuery<CareHomeDocumentListResponse>;

public sealed class GetMyCareHomeDocumentsQueryHandler(ICareHomesDbContext db, IDateTimeProvider clock)
    : IQueryHandler<GetMyCareHomeDocumentsQuery, CareHomeDocumentListResponse>
{
    public async Task<Result<CareHomeDocumentListResponse>> Handle(GetMyCareHomeDocumentsQuery request, CancellationToken ct)
    {
        CareHomeFacility? facility = await db.Facilities
            .Include(x => x.Revisions)
            .Include(x => x.Documents)
            .AsSplitQuery()
            .SingleOrDefaultAsync(x => x.OwnerUserId == request.ActorUserId, ct);
        if (facility is null)
            return Result<CareHomeDocumentListResponse>.Failure(CareHomeDocumentErrors.NotFound);
        return CareHomeDocumentListMapper.Map(facility, request.View, request.Type, request.IsExpired,
            CareHomePublicEligibility.CairoDate(clock.UtcNow));
    }
}

public sealed class GetAdminCareHomeDocumentsQueryHandler(ICareHomesDbContext db, IDateTimeProvider clock)
    : IQueryHandler<GetAdminCareHomeDocumentsQuery, CareHomeDocumentListResponse>
{
    public async Task<Result<CareHomeDocumentListResponse>> Handle(GetAdminCareHomeDocumentsQuery request, CancellationToken ct)
    {
        CareHomeFacility? facility = await db.Facilities.AsNoTracking()
            .Include(x => x.Revisions)
            .Include(x => x.Documents)
            .AsSplitQuery()
            .SingleOrDefaultAsync(x => x.Id == new CareHomeId(request.CareHomeId), ct);
        if (facility is null)
            return Result<CareHomeDocumentListResponse>.Failure(CareHomeAdminErrors.ApplicationNotFound);
        return CareHomeDocumentListMapper.Map(facility, request.View, request.Type, request.IsExpired,
            CareHomePublicEligibility.CairoDate(clock.UtcNow));
    }
}

/// <summary>Shared list mapping/validation used by both the owner and admin list routes.</summary>
internal static class CareHomeDocumentListMapper
{
    public static Result<CareHomeDocumentListResponse> Map(
        CareHomeFacility facility,
        string? view,
        CareHomeDocumentType? type,
        bool? isExpired,
        DateOnly cairoToday)
    {
        string normalized = string.IsNullOrWhiteSpace(view)
            ? CareHomeDocumentViews.All
            : view.Trim().ToLowerInvariant();
        if (normalized is not (CareHomeDocumentViews.All or CareHomeDocumentViews.Current or
            CareHomeDocumentViews.Pending or CareHomeDocumentViews.History))
            return Result<CareHomeDocumentListResponse>.Failure(CareHomeDocumentErrors.InvalidView);
        if (type is CareHomeDocumentType requested && !Enum.IsDefined(requested))
            return Result<CareHomeDocumentListResponse>.Failure(CareHomeDocumentErrors.InvalidType);

        IReadOnlyList<CareHomeDocument> active = CareHomeDocumentLifecycle.ActiveDocuments(facility);
        IReadOnlyDictionary<Guid, string> views = CareHomeDocumentLifecycle.Classify(
            facility.Documents, facility.ApprovedRevisionId,
            CareHomeDocumentLifecycle.ResolveTargetRevisionId(facility));

        CareHomeDocumentResponse[] items = active
            .Select(document => CareHomeDocumentLifecycle.ToResponse(document, views, cairoToday))
            .Where(item => normalized == CareHomeDocumentViews.All || item.LifecycleView == normalized)
            .Where(item => type is null || item.Type == type.Value)
            .Where(item => isExpired is null || item.IsExpired == isExpired.Value)
            .OrderByDescending(item => item.CreatedOnUtc)
            .ThenByDescending(item => item.Id)
            .ToArray();

        return Result<CareHomeDocumentListResponse>.Success(new(
            normalized, type, isExpired, items.Length, items));
    }
}

/// <summary>
/// Owner read of one of its own facility's private document files,
/// including history (current through superseded). Storage keys never
/// appear in responses; a logically removed or unknown document is 404.
/// The admin file route and its authorization are unchanged.
/// </summary>
public sealed record GetMyCareHomeDocumentFileQuery(UserId ActorUserId, Guid DocumentId)
    : IQuery<PrivateFileContent>;

public sealed class GetMyCareHomeDocumentFileQueryHandler(ICareHomesDbContext db, IFileStorage storage)
    : IQueryHandler<GetMyCareHomeDocumentFileQuery, PrivateFileContent>
{
    public async Task<Result<PrivateFileContent>> Handle(GetMyCareHomeDocumentFileQuery request, CancellationToken ct)
    {
        CareHomeFacility? facility = await db.Facilities.AsNoTracking()
            .Include(x => x.Documents)
            .SingleOrDefaultAsync(x => x.OwnerUserId == request.ActorUserId, ct);
        if (facility is null)
            return Result<PrivateFileContent>.Failure(CareHomeDocumentErrors.NotFound);
        CareHomeDocument? document = facility.Documents.SingleOrDefault(x =>
            x.Id == request.DocumentId && !x.IsRemoved);
        if (document is null)
            return Result<PrivateFileContent>.Failure(CareHomeDocumentErrors.DocumentNotFound);
        return await storage.OpenReadAsync(document.PrivateStorageKey, ct);
    }
}

/// <summary>
/// Owner history-only logical removal of one of its own facility's
/// documents. Logical only: the row and its shared bytes stay; the
/// aggregate stamps the audit columns and the read layer hides the row.
/// </summary>
public sealed record RemoveMyCareHomeDocumentCommand(UserId ActorUserId, int ExpectedVersion, Guid DocumentId) : ICommand;

public sealed class RemoveMyCareHomeDocumentCommandHandler(ICareHomesDbContext db, IDateTimeProvider clock)
    : ICommandHandler<RemoveMyCareHomeDocumentCommand>
{
    public async Task<Result> Handle(RemoveMyCareHomeDocumentCommand request, CancellationToken ct)
    {
        CareHomeFacility? facility = await db.Facilities
            .Include(x => x.Revisions)
            .Include(x => x.Documents)
            .AsSplitQuery()
            .SingleOrDefaultAsync(x => x.OwnerUserId == request.ActorUserId, ct);
        if (facility is null)
            return Result.Failure(CareHomeDocumentErrors.NotFound);
        CareHomeDocument? document = facility.Documents
            .SingleOrDefault(item => item.Id == request.DocumentId);
        if (document is null)
            return Result.Failure(CareHomeDocumentErrors.DocumentNotFound);
        // Idempotent replay: an already-removed owned document reports the
        // same success without a second write or a stale-version error.
        if (document.IsRemoved)
            return Result.Success();
        if (facility.Version != request.ExpectedVersion)
            return Result.Failure(CareHomeDocumentErrors.Conflict);

        // Recompute the classification at mutation time; never trust a
        // client-supplied view flag. Only the history class is removable.
        IReadOnlyDictionary<Guid, string> views = CareHomeDocumentLifecycle.Classify(
            facility.Documents,
            facility.ApprovedRevisionId,
            CareHomeDocumentLifecycle.ResolveTargetRevisionId(facility));
        if (CareHomeDocumentLifecycle.ViewOf(views, document.Id) != CareHomeDocumentViews.History)
            return Result.Failure(CareHomeDocumentErrors.NotRemovable);

        try
        {
            facility.RemoveOwnDocumentHistory(
                request.ActorUserId, request.ExpectedVersion, request.DocumentId, clock.UtcNow);
            await db.SaveChangesAsync(ct);
            return Result.Success();
        }
        catch (DomainException)
        {
            return Result.Failure(CareHomeDocumentErrors.Conflict);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(CareHomeDocumentErrors.Conflict);
        }
    }
}

/// <summary>Admin variant of the same history-only logical removal; the existing operational-admin policy covers the route.</summary>
public sealed record RemoveAdminCareHomeDocumentCommand(
    UserId ActorUserId,
    Guid CareHomeId,
    int ExpectedVersion,
    Guid DocumentId) : ICommand;

public sealed class RemoveAdminCareHomeDocumentCommandHandler(ICareHomesDbContext db, IDateTimeProvider clock)
    : ICommandHandler<RemoveAdminCareHomeDocumentCommand>
{
    public async Task<Result> Handle(RemoveAdminCareHomeDocumentCommand request, CancellationToken ct)
    {
        CareHomeFacility? facility = await db.Facilities
            .Include(x => x.Revisions)
            .Include(x => x.Documents)
            .AsSplitQuery()
            .SingleOrDefaultAsync(x => x.Id == new CareHomeId(request.CareHomeId), ct);
        if (facility is null)
            return Result.Failure(CareHomeAdminErrors.ApplicationNotFound);
        CareHomeDocument? document = facility.Documents
            .SingleOrDefault(item => item.Id == request.DocumentId);
        if (document is null)
            return Result.Failure(CareHomeAdminErrors.DocumentNotFound);
        if (document.IsRemoved)
            return Result.Success();
        if (facility.Version != request.ExpectedVersion)
            return Result.Failure(CareHomeAdminErrors.Conflict);

        IReadOnlyDictionary<Guid, string> views = CareHomeDocumentLifecycle.Classify(
            facility.Documents,
            facility.ApprovedRevisionId,
            CareHomeDocumentLifecycle.ResolveTargetRevisionId(facility));
        if (CareHomeDocumentLifecycle.ViewOf(views, document.Id) != CareHomeDocumentViews.History)
            return Result.Failure(CareHomeDocumentErrors.NotRemovable);

        try
        {
            facility.RemoveDocumentHistory(
                request.ActorUserId, request.ExpectedVersion, request.DocumentId, clock.UtcNow);
            await db.SaveChangesAsync(ct);
            return Result.Success();
        }
        catch (DomainException)
        {
            return Result.Failure(CareHomeAdminErrors.Conflict);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(CareHomeAdminErrors.Conflict);
        }
    }
}
