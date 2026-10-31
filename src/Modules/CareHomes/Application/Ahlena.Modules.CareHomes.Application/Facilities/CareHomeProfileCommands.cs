using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Abstractions;
using Ahlena.BuildingBlocks.Application.Abstractions.Storage;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Exceptions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.CareHomes.Application.Abstractions.Data;
using Ahlena.Modules.CareHomes.Application.Discovery;
using Ahlena.Modules.CareHomes.Domain.Facilities;

namespace Ahlena.Modules.CareHomes.Application.Facilities;

public sealed record CareHomeProfileRevisionProfile(Guid RevisionId, CareHomeProfileDraft Profile);

public sealed record CareHomeOwnerProfile(
    Guid Id,
    CareHomeStatus Status,
    int Version,
    Guid? SubmittedRevisionId,
    Guid? ApprovedRevisionId,
    Guid? OpenRevisionId,
    CareHomeProfileDraft? Draft,
    CareHomeProfileRevisionProfile? SubmittedProfile,
    CareHomeProfileRevisionProfile? ApprovedProfile,
    IReadOnlyList<CareHomeDocumentResponse> Documents,
    IReadOnlyList<CareHomeReviewHistory> ReviewHistory,
    IReadOnlyList<CareHomeProfileMediaResponse> ProfileMedia,
    // Additive 2026-10-27: the revision whose documents are the actionable
    // candidate set (the submitted revision while under review, else the
    // unfrozen editable revision). Clients use it to never act on a
    // historical revision; the domain's SubmittedRevisionId scoping remains
    // the enforcement.
    Guid? ReviewTargetRevisionId);

public sealed record CareHomeProfileMediaResponse(Guid Id, CareHomeProfileMediaKind Kind, int Position,
    string ContentType, long Length, Guid ProfileRevisionId, string FilePath);

public sealed record CreateCareHomeFacilityCommand(UserId ActorUserId) : ICommand<CareHomeOwnerProfile>;
public sealed record GetMyCareHomeFacilityQuery(UserId ActorUserId) : IQuery<CareHomeOwnerProfile>;
public sealed record SaveMyCareHomeProfileCommand(
    UserId ActorUserId,
    int ExpectedVersion,
    CareHomeProfileDraft Draft) : ICommand<CareHomeOwnerProfile>;
public sealed record SubmitMyCareHomeApplicationCommand(UserId ActorUserId, int ExpectedVersion)
    : ICommand<CareHomeOwnerProfile>;

public sealed class CreateCareHomeFacilityCommandHandler(ICareHomesDbContext db, IDateTimeProvider clock)
    : ICommandHandler<CreateCareHomeFacilityCommand, CareHomeOwnerProfile>
{
    public static readonly Error AlreadyExists = new("CareHomes.Facility.AlreadyExists", "This account already owns a care-home facility.");

    public async Task<Result<CareHomeOwnerProfile>> Handle(CreateCareHomeFacilityCommand request, CancellationToken ct)
    {
        if (request.ActorUserId == UserId.Empty)
            return Result<CareHomeOwnerProfile>.Failure(new Error("CareHomes.Facility.InvalidOwner", "Facility owner is required."));
        if (await db.Facilities.AnyAsync(x => x.OwnerUserId == request.ActorUserId, ct))
            return Result<CareHomeOwnerProfile>.Failure(AlreadyExists);

        CareHomeFacility facility = CareHomeFacility.CreateDraft(request.ActorUserId, DateTime.UtcNow);
        db.Facilities.Add(facility);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            if (await db.Facilities.AnyAsync(x => x.OwnerUserId == request.ActorUserId, ct))
                return Result<CareHomeOwnerProfile>.Failure(AlreadyExists);
            throw;
        }

        return Map(facility, media: null, CareHomePublicEligibility.CairoDate(clock.UtcNow));
    }

    internal static CareHomeOwnerProfile Map(CareHomeFacility facility, IReadOnlyList<CareHomeProfileMedia>? media, DateOnly cairoToday)
    {
        CareHomeProfileRevision? draft = facility.Revisions.LastOrDefault(x => !x.IsFrozen);
        CareHomeProfileDraft? profile = draft is null ? null : ToDraft(draft);
        IReadOnlyList<CareHomeDocument> activeDocuments = CareHomeDocumentLifecycle.ActiveDocuments(facility);
        IReadOnlyDictionary<Guid, string> documentViews = CareHomeDocumentLifecycle.Classify(
            facility.Documents, facility.ApprovedRevisionId,
            CareHomeDocumentLifecycle.ResolveTargetRevisionId(facility));
        return new CareHomeOwnerProfile(
            facility.Id.Value, facility.Status, facility.Version, facility.SubmittedRevisionId,
            facility.ApprovedRevisionId, draft?.Id, profile,
            ToRevisionProfile(facility, facility.SubmittedRevisionId, CareHomeStatus.PendingReview),
            ToRevisionProfile(facility, facility.ApprovedRevisionId, null),
            // Existing owner documents array: unchanged contents and
            // insertion order (minus logically removed rows), each item now
            // carrying the additive lifecycle/expiry/needs-action fields.
            facility.Documents.Where(document => !document.IsRemoved)
                .Select(document => CareHomeDocumentLifecycle.ToResponse(document, documentViews, cairoToday))
                .ToArray(),
            facility.ReviewHistory.ToArray(), MapMedia(media ?? []),
            CareHomeDocumentLifecycle.ResolveTargetRevisionId(facility));
    }

    internal static CareHomeProfileDraft ToDraft(CareHomeProfileRevision revision) => new(
        revision.ArabicName, revision.EnglishName, revision.ArabicDescription, revision.EnglishDescription,
        revision.ContactName, revision.ContactPhone, revision.ContactEmail, revision.Governorate, revision.City,
        revision.Area, revision.Address, revision.ArabicAdmissionConditions, revision.EnglishAdmissionConditions,
        revision.Amenities, revision.MedicalServices,
        Ahlena.Modules.CareHomes.Domain.Bookings.CareHomeCancellationTerms.Parse(revision.CancellationTermsJson));

    private static CareHomeProfileRevisionProfile? ToRevisionProfile(
        CareHomeFacility facility, Guid? revisionId, CareHomeStatus? requiredStatus)
    {
        if (revisionId is null) return null;
        if (requiredStatus is not null && facility.Status != requiredStatus) return null;
        CareHomeProfileRevision? revision = facility.Revisions.SingleOrDefault(x => x.Id == revisionId);
        return revision is null ? null : new CareHomeProfileRevisionProfile(revision.Id, ToDraft(revision));
    }

    internal static IReadOnlyList<CareHomeProfileMediaResponse> MapMedia(IEnumerable<CareHomeProfileMedia> media) =>
        media.OrderBy(x => x.Position).Select(x => new CareHomeProfileMediaResponse(x.Id, x.Kind, x.Position,
            x.ContentType, x.Length, x.ProfileRevisionId,
            $"/api/v1/care-homes/facilities/mine/media/{x.Id:D}/file")).ToArray();
}

public sealed class GetMyCareHomeFacilityQueryHandler(ICareHomesDbContext db, IDateTimeProvider clock)
    : IQueryHandler<GetMyCareHomeFacilityQuery, CareHomeOwnerProfile>
{
    private static readonly Error NotFound = new("CareHomes.Facility.NotFound", "Care-home facility was not found.");

    public async Task<Result<CareHomeOwnerProfile>> Handle(GetMyCareHomeFacilityQuery request, CancellationToken ct)
    {
        CareHomeFacility? facility = await db.Facilities
            .Include(x => x.Revisions)
            .Include(x => x.Documents)
            .Include(x => x.ReviewHistory)
            .AsSplitQuery()
            .SingleOrDefaultAsync(x => x.OwnerUserId == request.ActorUserId, ct);
        if (facility is null)
            return Result<CareHomeOwnerProfile>.Failure(NotFound);
        var media = await db.ProfileMedia.AsNoTracking()
            .Where(x => facility.Revisions.Select(r => r.Id).Contains(x.ProfileRevisionId)).ToListAsync(ct);
        return CareHomeStoredProfileMapper.Map(facility, media, CareHomePublicEligibility.CairoDate(clock.UtcNow));
    }
}

/// <summary>
/// Fail-closed read guard shared by the owner profile surfaces: a revision
/// whose stored structured cancellation terms cannot be re-read returns a
/// stable profile error instead of a 500. The owner save and the admin
/// review both validate the terms themselves, so only out-of-band corruption
/// can reach this guard.
/// </summary>
internal static class CareHomeStoredProfileMapper
{
    public static Result<CareHomeOwnerProfile> Map(
        CareHomeFacility facility, IReadOnlyList<CareHomeProfileMedia>? media, DateOnly cairoToday)
    {
        try
        {
            return CreateCareHomeFacilityCommandHandler.Map(facility, media, cairoToday);
        }
        catch (DomainException)
        {
            return Result<CareHomeOwnerProfile>.Failure(new Error("CareHomes.Facility.InvalidProfile",
                "The stored care-home profile could not be read; contact an administrator."));
        }
    }
}

public sealed class SaveMyCareHomeProfileCommandHandler(ICareHomesDbContext db, IDateTimeProvider clock)
    : ICommandHandler<SaveMyCareHomeProfileCommand, CareHomeOwnerProfile>
{
    private static readonly Error NotFound = new("CareHomes.Facility.NotFound", "Care-home facility was not found.");
    private static readonly Error InvalidProfile = new("CareHomes.Facility.InvalidProfile", "The care-home profile could not be saved in its current state.");
    private static readonly Error Conflict = new("CareHomes.Facility.Conflict", "The care-home profile changed; reload it before editing again.");

    public async Task<Result<CareHomeOwnerProfile>> Handle(SaveMyCareHomeProfileCommand request, CancellationToken ct)
    {
        CareHomeFacility? facility = await db.Facilities
            .Include(x => x.Revisions)
            .Include(x => x.Documents)
            .AsSplitQuery()
            .SingleOrDefaultAsync(x => x.OwnerUserId == request.ActorUserId, ct);
        if (facility is null)
            return Result<CareHomeOwnerProfile>.Failure(NotFound);
        if (facility.Version != request.ExpectedVersion)
            return Result<CareHomeOwnerProfile>.Failure(Conflict);

        bool hadEditableRevision = facility.Revisions.Any(revision => !revision.IsFrozen);
        CareHomeProfileRevision savedRevision;
        try
        {
            savedRevision = facility.SaveDraft(request.ActorUserId, request.ExpectedVersion, request.Draft, DateTime.UtcNow);
            if (!hadEditableRevision && facility.ApprovedRevisionId is Guid approvedRevisionId &&
                savedRevision.Id != approvedRevisionId)
            {
                var approvedMedia = await db.ProfileMedia
                    .Where(media => media.ProfileRevisionId == approvedRevisionId).ToListAsync(ct);
                foreach (var approvedImage in approvedMedia)
                    db.ProfileMedia.Add(CareHomeProfileMedia.Clone(approvedImage, savedRevision.Id));
            }
            await db.SaveChangesAsync(ct);
        }
        catch (DomainException)
        {
            return Result<CareHomeOwnerProfile>.Failure(InvalidProfile);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<CareHomeOwnerProfile>.Failure(Conflict);
        }

        var media = await db.ProfileMedia.AsNoTracking()
            .Where(x => facility.Revisions.Select(revision => revision.Id).Contains(x.ProfileRevisionId)).ToListAsync(ct);
        return CareHomeStoredProfileMapper.Map(facility, media, CareHomePublicEligibility.CairoDate(clock.UtcNow));
    }
}

public sealed class SubmitMyCareHomeApplicationCommandHandler(ICareHomesDbContext db, IDateTimeProvider clock)
    : ICommandHandler<SubmitMyCareHomeApplicationCommand, CareHomeOwnerProfile>
{
    private static readonly Error NotFound = new("CareHomes.Facility.NotFound", "Care-home facility was not found.");
    private static readonly Error Conflict = new("CareHomes.Facility.Conflict", "The care-home application changed; reload it before submitting.");
    private static readonly Error InvalidSubmission = new("CareHomes.Facility.InvalidSubmission", "The application requires a complete bilingual profile and all four required documents.");
    private static readonly Error ProfileMediaRequired = new("CareHomes.Facility.ProfileMediaRequired", "At least two facility photos including the cover are required before submission.");

    public async Task<Result<CareHomeOwnerProfile>> Handle(SubmitMyCareHomeApplicationCommand request, CancellationToken ct)
    {
        CareHomeFacility? facility = await db.Facilities
            .Include(x => x.Revisions)
            .Include(x => x.Documents)
            .AsSplitQuery()
            .SingleOrDefaultAsync(x => x.OwnerUserId == request.ActorUserId, ct);
        if (facility is null)
            return Result<CareHomeOwnerProfile>.Failure(NotFound);
        if (facility.Version != request.ExpectedVersion)
            return Result<CareHomeOwnerProfile>.Failure(Conflict);

        CareHomeProfileRevision? openRevision = facility.Revisions.SingleOrDefault(revision => !revision.IsFrozen);
        if (openRevision is not null)
        {
            int mediaCount = await db.ProfileMedia.CountAsync(x => x.ProfileRevisionId == openRevision.Id, ct);
            bool hasCover = await db.ProfileMedia.AnyAsync(x => x.ProfileRevisionId == openRevision.Id
                && x.Kind == CareHomeProfileMediaKind.Cover, ct);
            if (mediaCount < CareHomeFacility.MinimumProfileMediaCount || !hasCover)
                return Result<CareHomeOwnerProfile>.Failure(ProfileMediaRequired);
        }

        try
        {
            facility.Submit(request.ActorUserId, request.ExpectedVersion, DateTime.UtcNow);
            await db.SaveChangesAsync(ct);
        }
        catch (DomainException)
        {
            return Result<CareHomeOwnerProfile>.Failure(InvalidSubmission);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<CareHomeOwnerProfile>.Failure(Conflict);
        }

        var media = await db.ProfileMedia.AsNoTracking()
            .Where(x => facility.Revisions.Select(revision => revision.Id).Contains(x.ProfileRevisionId)).ToListAsync(ct);
        return CareHomeStoredProfileMapper.Map(facility, media, CareHomePublicEligibility.CairoDate(clock.UtcNow));
    }
}
