using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Abstractions.Storage;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Application.Abstractions.Data;
using Sanad.Modules.CareHomes.Domain.Facilities;

namespace Sanad.Modules.CareHomes.Application.Facilities;

public sealed record CareHomeOwnerProfile(
    Guid Id,
    CareHomeStatus Status,
    int Version,
    Guid? SubmittedRevisionId,
    Guid? ApprovedRevisionId,
    CareHomeProfileDraft? Draft,
    IReadOnlyList<CareHomeDocumentResponse> Documents,
    IReadOnlyList<CareHomeReviewHistory> ReviewHistory,
    IReadOnlyList<CareHomeProfileMediaResponse> ProfileMedia);

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

public sealed class CreateCareHomeFacilityCommandHandler(ICareHomesDbContext db)
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

        return Map(facility);
    }

    internal static CareHomeOwnerProfile Map(CareHomeFacility facility, IReadOnlyList<CareHomeProfileMedia>? media = null)
    {
        CareHomeProfileRevision? draft = facility.Revisions.LastOrDefault(x => !x.IsFrozen);
        CareHomeProfileDraft? profile = draft is null ? null : new CareHomeProfileDraft(
            draft.ArabicName, draft.EnglishName, draft.ArabicDescription, draft.EnglishDescription,
            draft.ContactName, draft.ContactPhone, draft.ContactEmail, draft.Governorate, draft.City,
            draft.Area, draft.Address, draft.ArabicAdmissionConditions, draft.EnglishAdmissionConditions,
            draft.Amenities, draft.MedicalServices);
        return new CareHomeOwnerProfile(
            facility.Id.Value, facility.Status, facility.Version, facility.SubmittedRevisionId,
            facility.ApprovedRevisionId, profile,
            facility.Documents.Select(UploadCareHomeDocumentCommandHandler.ToResponse).ToArray(),
            facility.ReviewHistory.ToArray(), MapMedia(media ?? []));
    }

    internal static IReadOnlyList<CareHomeProfileMediaResponse> MapMedia(IEnumerable<CareHomeProfileMedia> media) =>
        media.OrderBy(x => x.Position).Select(x => new CareHomeProfileMediaResponse(x.Id, x.Kind, x.Position,
            x.ContentType, x.Length, x.ProfileRevisionId,
            $"/api/v1/care-homes/facilities/mine/media/{x.Id:D}/file")).ToArray();
}

public sealed class GetMyCareHomeFacilityQueryHandler(ICareHomesDbContext db)
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
        return CreateCareHomeFacilityCommandHandler.Map(facility, media);
    }
}

public sealed class SaveMyCareHomeProfileCommandHandler(ICareHomesDbContext db)
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

        try
        {
            facility.SaveDraft(request.ActorUserId, request.ExpectedVersion, request.Draft, DateTime.UtcNow);
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
        return CreateCareHomeFacilityCommandHandler.Map(facility, media);
    }
}

public sealed class SubmitMyCareHomeApplicationCommandHandler(ICareHomesDbContext db)
    : ICommandHandler<SubmitMyCareHomeApplicationCommand, CareHomeOwnerProfile>
{
    private static readonly Error NotFound = new("CareHomes.Facility.NotFound", "Care-home facility was not found.");
    private static readonly Error Conflict = new("CareHomes.Facility.Conflict", "The care-home application changed; reload it before submitting.");
    private static readonly Error InvalidSubmission = new("CareHomes.Facility.InvalidSubmission", "The application requires a complete bilingual profile and all four required documents.");

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
        return CreateCareHomeFacilityCommandHandler.Map(facility, media);
    }
}
