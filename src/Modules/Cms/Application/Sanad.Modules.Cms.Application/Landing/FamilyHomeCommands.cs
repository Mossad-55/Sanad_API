using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;
using Sanad.Modules.Cms.Application.Abstractions.Data;
using Sanad.Modules.Cms.Domain.Landing;

namespace Sanad.Modules.Cms.Application.Landing;

/// <summary>
/// Version precondition shared by every mutating handler:
/// <c>0</c> means "no precondition, use the current version", any positive
/// value must equal the current version of the row the edit applies to, and a
/// negative value is a malformed request.
/// </summary>
internal static class FamilyHomeVersions
{
    public static bool TryResolve(int expectedVersion, int currentVersion, out int resolvedVersion)
    {
        resolvedVersion = currentVersion;

        if (expectedVersion < 0)
            return false;

        if (expectedVersion == 0)
            return true;

        resolvedVersion = expectedVersion;
        return expectedVersion == currentVersion;
    }
}

public sealed record CreateFamilyHomeSectionCommand(
    FamilyHomeSectionKind Kind,
    IReadOnlyList<FamilyHomeItemRequest>? Items,
    int DisplayOrder,
    bool IsVisible)
    : ICommand<FamilyHomeAdminSectionResponse>;

public sealed class CreateFamilyHomeSectionCommandHandler(ICmsDbContext db)
    : ICommandHandler<CreateFamilyHomeSectionCommand, FamilyHomeAdminSectionResponse>
{
    public async Task<Result<FamilyHomeAdminSectionResponse>> Handle(
        CreateFamilyHomeSectionCommand request, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(request.Kind))
            return FamilyHomeErrors.InvalidKind;
        if (request.DisplayOrder < 0)
            return FamilyHomeErrors.SectionInvalid;

        IReadOnlyList<FamilyHomeItem> items = FamilyHomeItems.Normalize(request.Items);
        if (FamilyHomeContentRules.ValidateContent(request.Kind, items) is not null)
            return FamilyHomeErrors.SectionInvalid;

        try
        {
            FamilyHomeSection section = FamilyHomeSection.Create(
                request.Kind,
                FamilyHomeItems.Serialize(items),
                request.DisplayOrder,
                request.IsVisible,
                sourceId: null,
                DateTime.UtcNow);

            db.FamilyHomeSections.Add(section);
            await db.SaveChangesAsync(cancellationToken);

            return section.ToAdminResponse();
        }
        catch (DomainException)
        {
            return FamilyHomeErrors.SectionInvalid;
        }
    }
}

public sealed record UpdateFamilyHomeSectionCommand(
    Guid Id,
    IReadOnlyList<FamilyHomeItemRequest>? Items,
    int DisplayOrder,
    bool IsVisible,
    int ExpectedVersion)
    : ICommand<FamilyHomeAdminSectionResponse>;

public sealed class UpdateFamilyHomeSectionCommandHandler(ICmsDbContext db)
    : ICommandHandler<UpdateFamilyHomeSectionCommand, FamilyHomeAdminSectionResponse>
{
    public async Task<Result<FamilyHomeAdminSectionResponse>> Handle(
        UpdateFamilyHomeSectionCommand request, CancellationToken cancellationToken)
    {
        FamilyHomeSection? section = await db.FamilyHomeSections
            .SingleOrDefaultAsync(item => item.Id == new FamilyHomeSectionId(request.Id), cancellationToken);
        if (section is null)
            return FamilyHomeErrors.SectionNotFound;
        if (request.DisplayOrder < 0 || request.ExpectedVersion < 0)
            return FamilyHomeErrors.SectionInvalid;
        if (section.Status == FamilyHomeSectionStatus.Archived)
            return FamilyHomeErrors.ImmutableHistory;

        IReadOnlyList<FamilyHomeItem> items = FamilyHomeItems.Normalize(request.Items);
        if (FamilyHomeContentRules.ValidateContent(section.Kind, items) is not null)
            return FamilyHomeErrors.SectionInvalid;

        string serialized = FamilyHomeItems.Serialize(items);

        try
        {
            if (section.Status == FamilyHomeSectionStatus.Draft)
            {
                if (!FamilyHomeVersions.TryResolve(request.ExpectedVersion, section.Version, out int expectedVersion))
                    return FamilyHomeErrors.StaleVersion;

                section.UpdateDraft(serialized, request.DisplayOrder, request.IsVisible, expectedVersion);
                await db.SaveChangesAsync(cancellationToken);

                return section.ToAdminResponse();
            }

            FamilyHomeSection? twin = await db.FamilyHomeSections
                .SingleOrDefaultAsync(
                    item => item.SourceId == section.Id &&
                        item.Status == FamilyHomeSectionStatus.Draft,
                    cancellationToken);

            // The precondition names the row being edited: the draft twin when
            // one exists, otherwise the published row that is forked now.
            if (!FamilyHomeVersions.TryResolve(
                    request.ExpectedVersion,
                    twin?.Version ?? section.Version,
                    out int twinVersion))
            {
                return FamilyHomeErrors.StaleVersion;
            }

            if (twin is null)
            {
                twin = FamilyHomeSection.Create(
                    section.Kind,
                    serialized,
                    request.DisplayOrder,
                    request.IsVisible,
                    section.Id,
                    DateTime.UtcNow);
                db.FamilyHomeSections.Add(twin);
            }
            else
            {
                twin.UpdateDraft(serialized, request.DisplayOrder, request.IsVisible, twinVersion);
            }

            await db.SaveChangesAsync(cancellationToken);

            return twin.ToAdminResponse();
        }
        catch (DomainException)
        {
            return FamilyHomeErrors.SectionInvalid;
        }
        catch (DbUpdateConcurrencyException)
        {
            return FamilyHomeErrors.StaleVersion;
        }
    }
}

public sealed record PublishFamilyHomeSectionCommand(Guid Id, Guid ActorUserId, int ExpectedVersion)
    : ICommand<FamilyHomeAdminSectionResponse>;

public sealed class PublishFamilyHomeSectionCommandHandler(
    ICmsDbContext db,
    ICaregiversDbContext caregiversDb)
    : ICommandHandler<PublishFamilyHomeSectionCommand, FamilyHomeAdminSectionResponse>
{
    public async Task<Result<FamilyHomeAdminSectionResponse>> Handle(
        PublishFamilyHomeSectionCommand request, CancellationToken cancellationToken)
    {
        FamilyHomeSection? section = await db.FamilyHomeSections
            .SingleOrDefaultAsync(item => item.Id == new FamilyHomeSectionId(request.Id), cancellationToken);
        if (section is null)
            return FamilyHomeErrors.SectionNotFound;
        if (request.ActorUserId == Guid.Empty || request.ExpectedVersion < 0)
            return FamilyHomeErrors.SectionInvalid;
        if (section.Status == FamilyHomeSectionStatus.Archived)
            return FamilyHomeErrors.ImmutableHistory;
        if (section.Status != FamilyHomeSectionStatus.Draft)
            return FamilyHomeErrors.SectionInvalid;

        if (!FamilyHomeVersions.TryResolve(request.ExpectedVersion, section.Version, out int expectedVersion))
            return FamilyHomeErrors.StaleVersion;

        IReadOnlyList<FamilyHomeItem> items;
        try
        {
            items = FamilyHomeItems.Deserialize(section.ItemsJson);
        }
        catch (DomainException ex)
        {
            return FamilyHomeErrors.Incomplete(ex.Message);
        }

        if (FamilyHomeContentRules.ValidateContent(section.Kind, items) is not null)
            return FamilyHomeErrors.SectionInvalid;

        string? gap = FamilyHomeContentRules.ValidatePublishedContent(section.Kind, items);
        if (gap is not null)
            return FamilyHomeErrors.Incomplete(gap);

        // Referenced catalog services must exist and be active at publication.
        // A reference that becomes missing or inactive later is omitted by the
        // Family read instead of failing the response.
        if (!await ReferencedServicesAreActiveAsync(section.Kind, items, cancellationToken))
            return FamilyHomeErrors.Incomplete("a referenced catalog service is missing or inactive.");

        try
        {
            section.Publish(expectedVersion);
        }
        catch (DomainException ex)
        {
            return FamilyHomeErrors.Incomplete(ex.Message);
        }

        List<FamilyHomeSection> superseded = await db.FamilyHomeSections
            .Where(item => item.Kind == section.Kind &&
                item.Status == FamilyHomeSectionStatus.Published &&
                item.Id != section.Id)
            .ToListAsync(cancellationToken);
        foreach (FamilyHomeSection previous in superseded)
            previous.Archive();

        db.FamilyHomeSectionHistory.Add(
            FamilyHomeSectionHistory.Create(
                section.Id,
                LandingHistoryAction.Published,
                new UserId(request.ActorUserId),
                createdOnUtc: DateTime.UtcNow));

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return FamilyHomeErrors.StaleVersion;
        }

        return section.ToAdminResponse();
    }

    private async Task<bool> ReferencedServicesAreActiveAsync(
        FamilyHomeSectionKind kind,
        IReadOnlyList<FamilyHomeItem> items,
        CancellationToken cancellationToken)
    {
        var referenced = new List<Guid>();
        if (kind == FamilyHomeSectionKind.FeaturedServices)
            referenced.AddRange(FamilyHomeContentRules.FeaturedServiceIds(items));
        else
            referenced.AddRange(FamilyHomeContentRules.TargetServiceIds(items));

        if (referenced.Count == 0)
            return true;

        List<ServiceId> serviceIds = referenced.Select(id => new ServiceId(id)).ToList();
        List<Service> services = await caregiversDb.Services
            .AsNoTracking()
            .Where(service => serviceIds.Contains(service.Id))
            .ToListAsync(cancellationToken);

        return referenced.All(id => services.Any(
            service => service.Id.Value == id && service.IsActive));
    }
}

public sealed record SetFamilyHomeSectionVisibilityCommand(
    Guid Id,
    bool IsVisible,
    Guid ActorUserId,
    int ExpectedVersion)
    : ICommand;

public sealed class SetFamilyHomeSectionVisibilityCommandHandler(ICmsDbContext db)
    : ICommandHandler<SetFamilyHomeSectionVisibilityCommand>
{
    public async Task<Result> Handle(
        SetFamilyHomeSectionVisibilityCommand request, CancellationToken cancellationToken)
    {
        FamilyHomeSection? section = await db.FamilyHomeSections
            .SingleOrDefaultAsync(item => item.Id == new FamilyHomeSectionId(request.Id), cancellationToken);
        if (section is null)
            return Result.Failure(FamilyHomeErrors.SectionNotFound);
        if (request.ActorUserId == Guid.Empty || request.ExpectedVersion < 0)
            return Result.Failure(FamilyHomeErrors.SectionInvalid);
        if (section.Status == FamilyHomeSectionStatus.Archived)
            return Result.Failure(FamilyHomeErrors.ImmutableHistory);

        if (!FamilyHomeVersions.TryResolve(request.ExpectedVersion, section.Version, out int expectedVersion))
            return Result.Failure(FamilyHomeErrors.StaleVersion);

        try
        {
            section.SetVisibility(request.IsVisible, expectedVersion);

            db.FamilyHomeSectionHistory.Add(
                FamilyHomeSectionHistory.Create(
                    section.Id,
                    LandingHistoryAction.VisibilityChanged,
                    new UserId(request.ActorUserId),
                    $"IsVisible={request.IsVisible}",
                    DateTime.UtcNow));

            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DomainException)
        {
            return Result.Failure(FamilyHomeErrors.SectionInvalid);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(FamilyHomeErrors.StaleVersion);
        }

        return Result.Success();
    }
}

public sealed record DeleteFamilyHomeSectionCommand(Guid Id, Guid ActorUserId)
    : ICommand;

public sealed class DeleteFamilyHomeSectionCommandHandler(ICmsDbContext db)
    : ICommandHandler<DeleteFamilyHomeSectionCommand>
{
    public async Task<Result> Handle(
        DeleteFamilyHomeSectionCommand request, CancellationToken cancellationToken)
    {
        FamilyHomeSection? section = await db.FamilyHomeSections
            .SingleOrDefaultAsync(item => item.Id == new FamilyHomeSectionId(request.Id), cancellationToken);
        if (section is null)
            return Result.Failure(FamilyHomeErrors.SectionNotFound);
        if (request.ActorUserId == Guid.Empty)
            return Result.Failure(FamilyHomeErrors.SectionInvalid);

        // Published and archived sections are retained history: only a draft is
        // deletable, and deleting a draft twin never touches its published row.
        if (section.Status != FamilyHomeSectionStatus.Draft)
            return Result.Failure(FamilyHomeErrors.PublishedHistoryConflict);

        string details = $"Kind={section.Kind.ToKindKey()}";
        if (section.SourceId is not null)
            details += $";TwinOf={section.SourceId.Value.Value}";

        db.FamilyHomeSections.Remove(section);
        db.FamilyHomeSectionHistory.Add(
            FamilyHomeSectionHistory.Create(
                section.Id,
                LandingHistoryAction.Deleted,
                new UserId(request.ActorUserId),
                details,
                DateTime.UtcNow));

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(FamilyHomeErrors.StaleVersion);
        }

        return Result.Success();
    }
}

public sealed record ReorderFamilyHomeSectionsCommand(IReadOnlyList<FamilyHomeReorderItem> Items)
    : ICommand;

public sealed class ReorderFamilyHomeSectionsCommandHandler(ICmsDbContext db)
    : ICommandHandler<ReorderFamilyHomeSectionsCommand>
{
    public async Task<Result> Handle(
        ReorderFamilyHomeSectionsCommand request, CancellationToken cancellationToken)
    {
        if (request.Items.Count == 0 ||
            request.Items.Any(item => item.Id == Guid.Empty || item.DisplayOrder < 0) ||
            request.Items.Select(item => item.Id).Distinct().Count() != request.Items.Count)
        {
            return Result.Failure(FamilyHomeErrors.ReorderInvalid);
        }

        List<FamilyHomeSection> sections = await db.FamilyHomeSections.ToListAsync(cancellationToken);
        Dictionary<Guid, FamilyHomeSection> byId = sections.ToDictionary(section => section.Id.Value);

        if (request.Items.Any(item => !byId.ContainsKey(item.Id)))
            return Result.Failure(FamilyHomeErrors.ReorderInvalid);

        try
        {
            foreach (FamilyHomeReorderItem item in request.Items)
                byId[item.Id].SetDisplayOrder(item.DisplayOrder);

            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(FamilyHomeErrors.StaleVersion);
        }

        return Result.Success();
    }
}
