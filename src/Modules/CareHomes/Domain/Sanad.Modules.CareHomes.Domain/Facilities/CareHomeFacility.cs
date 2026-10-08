using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.CareHomes.Domain.Facilities;

public sealed class CareHomeFacility : AggregateRoot<CareHomeId>
{
    private readonly List<CareHomeProfileRevision> _revisions = [];
    private readonly List<CareHomeDocument> _documents = [];
    private readonly List<CareHomeReviewHistory> _reviewHistory = [];

    private CareHomeFacility()
    {
    }

    private CareHomeFacility(CareHomeId id, UserId ownerUserId, DateTime utcNow)
        : base(id)
    {
        OwnerUserId = ownerUserId;
        Status = CareHomeStatus.Draft;
        CreatedOnUtc = utcNow;
        UpdatedOnUtc = utcNow;
        Version = 1;
    }

    public UserId OwnerUserId { get; private set; }
    public CareHomeStatus Status { get; private set; }
    public Guid? SubmittedRevisionId { get; private set; }
    public Guid? ApprovedRevisionId { get; private set; }
    public int Version { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }
    public DateTime UpdatedOnUtc { get; private set; }
    public IReadOnlyCollection<CareHomeProfileRevision> Revisions => _revisions.AsReadOnly();
    public IReadOnlyCollection<CareHomeDocument> Documents => _documents.AsReadOnly();
    public IReadOnlyCollection<CareHomeReviewHistory> ReviewHistory => _reviewHistory.AsReadOnly();

    public void EnsureInventoryAccess(UserId actorUserId, int expectedVersion)
    {
        EnsureOwner(actorUserId);
        EnsureVersion(expectedVersion);
        if (Status is CareHomeStatus.PendingReview or CareHomeStatus.Suspended)
            throw new DomainException("Inventory cannot be changed while the facility is under review or suspended.");
    }

    public void RecordInventoryChange(DateTime utcNow)
    {
        EnsureUtc(utcNow);
        UpdatedOnUtc = utcNow;
        Version++;
    }

    public static CareHomeFacility CreateDraft(UserId ownerUserId, DateTime utcNow)
    {
        if (ownerUserId == UserId.Empty)
            throw new DomainException("Care-home owner is required.");
        EnsureUtc(utcNow);
        return new CareHomeFacility(CareHomeId.New(), ownerUserId, utcNow);
    }

    public CareHomeProfileRevision SaveDraft(
        UserId actorUserId,
        int expectedVersion,
        CareHomeProfileDraft draft,
        DateTime utcNow)
    {
        EnsureOwner(actorUserId);
        EnsureVersion(expectedVersion);
        EnsureUtc(utcNow);
        if (Status is not (CareHomeStatus.Draft or CareHomeStatus.NeedsCorrection or CareHomeStatus.Rejected or CareHomeStatus.Approved))
            throw new DomainException("A submitted or approved care-home profile cannot be edited in place.");

        CareHomeProfileRevision? current = _revisions.LastOrDefault(revision => !revision.IsFrozen);
        if (current is null)
        {
            current = CareHomeProfileRevision.Create(_revisions.Count + 1, actorUserId, draft, utcNow);
            _revisions.Add(current);
        }
        else
        {
            current.UpdateDraft(draft, utcNow);
        }

        if (ApprovedRevisionId is null)
            Status = CareHomeStatus.Draft;
        UpdatedOnUtc = utcNow;
        Version++;
        return current;
    }

    public CareHomeDocument UploadDocument(
        UserId actorUserId,
        CareHomeDocumentType type,
        string privateStorageKey,
        string contentType,
        long length,
        DateOnly? expiryDate,
        DateTime utcNow)
        => UploadDocument(actorUserId, null, type, privateStorageKey, contentType, length, expiryDate, utcNow);

    public CareHomeDocument UploadDocument(
        UserId actorUserId,
        int expectedVersion,
        CareHomeDocumentType type,
        string privateStorageKey,
        string contentType,
        long length,
        DateOnly? expiryDate,
        DateTime utcNow)
        => UploadDocument(actorUserId, (int?)expectedVersion, type, privateStorageKey, contentType, length, expiryDate, utcNow);

    private CareHomeDocument UploadDocument(
        UserId actorUserId,
        int? expectedVersion,
        CareHomeDocumentType type,
        string privateStorageKey,
        string contentType,
        long length,
        DateOnly? expiryDate,
        DateTime utcNow)
    {
        EnsureOwner(actorUserId);
        if (expectedVersion is not null)
            EnsureVersion(expectedVersion.Value);
        EnsureUtc(utcNow);
        if (Status is CareHomeStatus.PendingReview or CareHomeStatus.Suspended)
            throw new DomainException("Submitted care-home revisions are frozen; upload a replacement while editing a draft or correction.");
        CareHomeProfileRevision revision = _revisions.LastOrDefault(item => !item.IsFrozen)
            ?? throw new DomainException("Save a care-home profile draft before uploading its documents.");

        CareHomeDocument document = CareHomeDocument.Upload(
            type, revision.Id, privateStorageKey, contentType, length, expiryDate, utcNow);
        _documents.Add(document);
        UpdatedOnUtc = utcNow;
        Version++;
        return document;
    }

    public CareHomeProfileRevision PrepareProfileMediaRevision(UserId actorUserId, int expectedVersion, DateTime utcNow)
    {
        EnsureOwner(actorUserId);
        EnsureVersion(expectedVersion);
        EnsureUtc(utcNow);
        if (Status is CareHomeStatus.PendingReview or CareHomeStatus.Suspended)
            throw new DomainException("Profile media cannot be changed while the facility is under review or suspended.");

        CareHomeProfileRevision? editable = _revisions.LastOrDefault(revision => !revision.IsFrozen);
        if (editable is not null)
            return editable;

        CareHomeProfileRevision? source = ApprovedRevisionId is Guid approvedId
            ? _revisions.SingleOrDefault(revision => revision.Id == approvedId)
            : _revisions.OrderByDescending(revision => revision.RevisionNumber).FirstOrDefault();
        if (source is null)
            throw new DomainException("Save a profile draft before adding facility media.");

        editable = CareHomeProfileRevision.Create(_revisions.Count + 1, actorUserId, source.ToDraft(), utcNow);
        _revisions.Add(editable);
        UpdatedOnUtc = utcNow;
        Version++;
        return editable;
    }

    public void RecordProfileMediaChange(UserId actorUserId, int expectedVersion, DateTime utcNow)
    {
        EnsureOwner(actorUserId);
        EnsureVersion(expectedVersion);
        EnsureUtc(utcNow);
        if (Status is CareHomeStatus.PendingReview or CareHomeStatus.Suspended)
            throw new DomainException("Profile media cannot be changed while the facility is under review or suspended.");
        if (!_revisions.Any(revision => !revision.IsFrozen))
            throw new DomainException("An editable profile revision is required for facility media.");
        UpdatedOnUtc = utcNow;
        Version++;
    }

    public void Submit(UserId actorUserId, int expectedVersion, DateTime utcNow)
    {
        EnsureOwner(actorUserId);
        EnsureVersion(expectedVersion);
        EnsureUtc(utcNow);
        if (Status is not (CareHomeStatus.Draft or CareHomeStatus.NeedsCorrection or CareHomeStatus.Rejected or CareHomeStatus.Approved))
            throw new DomainException("Only a draft, correction, or rejected care-home application can be submitted.");
        CareHomeProfileRevision revision = _revisions.LastOrDefault(item => !item.IsFrozen)
            ?? throw new DomainException("A care-home profile draft is required before submission.");
        if (revision.Id == ApprovedRevisionId)
            throw new DomainException("Create a new profile revision before resubmitting an approved facility.");
        var required = Enum.GetValues<CareHomeDocumentType>();
        if (required.Any(type => !_documents.Any(document => document.Type == type && document.ProfileRevisionId == revision.Id)))
            throw new DomainException("Operating license, registration, health, and civil-defense documents are required before submission.");

        revision.Freeze(utcNow);
        SubmittedRevisionId = revision.Id;
        Status = CareHomeStatus.PendingReview;
        UpdatedOnUtc = utcNow;
        Version++;
    }

    public void VerifyDocument(
        UserId adminUserId,
        int expectedVersion,
        Guid documentId,
        DateOnly? expiryDate,
        bool confirmNonExpiring,
        DateTime utcNow)
    {
        EnsureVersion(expectedVersion);
        if (adminUserId == UserId.Empty)
            throw new DomainException("Care-home document verifier is required.");
        if (Status != CareHomeStatus.PendingReview || SubmittedRevisionId is null)
            throw new DomainException("Documents can only be verified while the application is under review.");
        CareHomeDocument document = _documents.SingleOrDefault(item =>
                item.Id == documentId && item.ProfileRevisionId == SubmittedRevisionId.Value)
            ?? throw new DomainException("Care-home document was not found in the submitted revision.");
        document.Verify(adminUserId, expiryDate, confirmNonExpiring, utcNow);
        UpdatedOnUtc = utcNow;
        Version++;
    }

    public void RejectDocument(UserId adminUserId, int expectedVersion, Guid documentId, string reason, DateTime utcNow)
    {
        EnsureVersion(expectedVersion);
        if (adminUserId == UserId.Empty)
            throw new DomainException("Care-home document reviewer is required.");
        if (Status != CareHomeStatus.PendingReview || SubmittedRevisionId is null)
            throw new DomainException("Documents can only be rejected while the application is under review.");
        CareHomeDocument document = _documents.SingleOrDefault(item =>
                item.Id == documentId && item.ProfileRevisionId == SubmittedRevisionId.Value)
            ?? throw new DomainException("Care-home document was not found in the submitted revision.");
        document.Reject(adminUserId, reason, utcNow);
        UpdatedOnUtc = utcNow;
        Version++;
    }

    public CareHomeReviewHistory Review(
        UserId adminUserId,
        int expectedVersion,
        CareHomeReviewAction action,
        string? reason,
        DateTime utcNow,
        DateOnly currentDate)
    {
        EnsureVersion(expectedVersion);
        EnsureUtc(utcNow);
        if (adminUserId == UserId.Empty)
            throw new DomainException("Care-home reviewer is required.");

        CareHomeStatus previous = Status;
        Guid? revisionId = SubmittedRevisionId;
        switch (action)
        {
            case CareHomeReviewAction.Approved:
                if (Status != CareHomeStatus.PendingReview || revisionId is null)
                    throw new DomainException("Only a submitted care-home application can be approved.");
                if (_documents.Where(document => document.ProfileRevisionId == revisionId)
                    .GroupBy(document => document.Type)
                    .Any(group => !LatestDocument(group).IsUsableOn(currentDate)) ||
                    Enum.GetValues<CareHomeDocumentType>().Any(type => !_documents.Any(document =>
                        document.ProfileRevisionId == revisionId && document.Type == type && document.IsUsableOn(currentDate))))
                    throw new DomainException("Every required care-home document must be Admin-verified and valid before approval.");
                CareHomeProfileRevision submitted = _revisions.Single(revision => revision.Id == revisionId);
                submitted.MarkApproved(utcNow);
                ApprovedRevisionId = revisionId;
                Status = CareHomeStatus.Approved;
                break;
            case CareHomeReviewAction.Rejected:
            case CareHomeReviewAction.CorrectionsRequested:
                if (Status != CareHomeStatus.PendingReview)
                    throw new DomainException("Only a submitted care-home application can be rejected or returned for correction.");
                RequireReason(reason);
                Status = action == CareHomeReviewAction.Rejected ? CareHomeStatus.Rejected : CareHomeStatus.NeedsCorrection;
                SubmittedRevisionId = null;
                break;
            case CareHomeReviewAction.Suspended:
                if (Status != CareHomeStatus.Approved)
                    throw new DomainException("Only an approved care home can be suspended.");
                RequireReason(reason);
                Status = CareHomeStatus.Suspended;
                break;
            case CareHomeReviewAction.Reactivated:
                if (Status != CareHomeStatus.Suspended)
                    throw new DomainException("Only a suspended care home can be reactivated.");
                if (ApprovedRevisionId is null || _documents.Where(document => document.ProfileRevisionId == ApprovedRevisionId.Value)
                    .GroupBy(document => document.Type)
                    .Any(group => !LatestDocument(group).IsUsableOn(currentDate)))
                    throw new DomainException("Required care-home documents must be valid before reactivation.");
                Status = CareHomeStatus.Approved;
                break;
            default:
                throw new DomainException("Care-home review action is invalid.");
        }

        CareHomeReviewHistory history = new(
            Guid.CreateVersion7(), revisionId, action, previous, Status,
            adminUserId, reason?.Trim(), utcNow);
        _reviewHistory.Add(history);
        UpdatedOnUtc = utcNow;
        Version++;
        return history;
    }

    private void EnsureOwner(UserId actorUserId)
    {
        if (actorUserId == UserId.Empty || actorUserId != OwnerUserId)
            throw new DomainException("Care-home profile actions are limited to the facility owner.");
    }

    private static CareHomeDocument LatestDocument(IEnumerable<CareHomeDocument> documents) =>
        documents.OrderByDescending(document => document.CreatedOnUtc)
            .ThenByDescending(document => document.Id)
            .First();

    private void EnsureVersion(int expectedVersion)
    {
        if (expectedVersion != Version)
            throw new DomainException("Care-home application version is stale.");
    }

    private static void RequireReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 1000)
            throw new DomainException("A review reason of at most 1000 characters is required.");
    }

    private static void EnsureUtc(DateTime utcNow)
    {
        if (utcNow.Kind != DateTimeKind.Utc)
            throw new DomainException("Care-home timestamps must be UTC.");
    }
}

public sealed class CareHomeReviewHistory : Entity<Guid>
{
    internal CareHomeReviewHistory(
        Guid id,
        Guid? profileRevisionId,
        CareHomeReviewAction action,
        CareHomeStatus previousStatus,
        CareHomeStatus newStatus,
        UserId actorUserId,
        string? reason,
        DateTime occurredOnUtc)
        : base(id)
    {
        ProfileRevisionId = profileRevisionId;
        Action = action;
        PreviousStatus = previousStatus;
        NewStatus = newStatus;
        ActorUserId = actorUserId;
        Reason = reason;
        OccurredOnUtc = occurredOnUtc;
    }

    private CareHomeReviewHistory() { }
    public Guid? ProfileRevisionId { get; private set; }
    public CareHomeReviewAction Action { get; private set; }
    public CareHomeStatus PreviousStatus { get; private set; }
    public CareHomeStatus NewStatus { get; private set; }
    public UserId ActorUserId { get; private set; }
    public string? Reason { get; private set; }
    public DateTime OccurredOnUtc { get; private set; }
}
