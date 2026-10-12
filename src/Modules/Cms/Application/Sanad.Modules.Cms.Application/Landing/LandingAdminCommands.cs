using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Cms.Application.Abstractions.Data;
using Sanad.Modules.Cms.Domain.Landing;
using Sanad.Modules.Families.Application.Abstractions.Data;

namespace Sanad.Modules.Cms.Application.Landing;

public sealed record CreateLandingSectionCommand(
    LandingSectionType Type,
    string? ArabicTitle,
    string? EnglishTitle,
    string? ArabicBody,
    string? EnglishBody,
    string? ContentJson,
    string? ImagePath,
    int DisplayOrder,
    bool IsVisible)
    : ICommand<LandingAdminSectionResponse>;

public sealed class CreateLandingSectionCommandHandler(
    ICmsDbContext db,
    IFamiliesDbContext familiesDb,
    ICaregiversDbContext caregiversDb)
    : ICommandHandler<CreateLandingSectionCommand, LandingAdminSectionResponse>
{
    public async Task<Result<LandingAdminSectionResponse>> Handle(
        CreateLandingSectionCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var section = LandingSection.Create(
                request.Type,
                request.ArabicTitle,
                request.EnglishTitle,
                request.ArabicBody,
                request.EnglishBody,
                request.ContentJson,
                request.ImagePath,
                request.DisplayOrder,
                request.IsVisible,
                sourceId: null,
                DateTime.UtcNow);

            db.LandingSections.Add(section);
            await db.SaveChangesAsync(cancellationToken);

            return section.ToAdminResponse(
                await LandingAdminWarnings.CountAsync(db, familiesDb, caregiversDb, section, cancellationToken));
        }
        catch (DomainException)
        {
            return LandingErrors.SectionInvalid;
        }
    }
}

public sealed record UpdateLandingSectionCommand(
    Guid Id,
    string? ArabicTitle,
    string? EnglishTitle,
    string? ArabicBody,
    string? EnglishBody,
    string? ContentJson,
    string? ImagePath,
    int DisplayOrder,
    bool IsVisible)
    : ICommand<LandingAdminSectionResponse>;

public sealed class UpdateLandingSectionCommandHandler(
    ICmsDbContext db,
    IFamiliesDbContext familiesDb,
    ICaregiversDbContext caregiversDb)
    : ICommandHandler<UpdateLandingSectionCommand, LandingAdminSectionResponse>
{
    public async Task<Result<LandingAdminSectionResponse>> Handle(
        UpdateLandingSectionCommand request, CancellationToken cancellationToken)
    {
        LandingSection? section = await db.LandingSections
            .SingleOrDefaultAsync(
                item => item.Id == new LandingSectionId(request.Id), cancellationToken);
        if (section is null)
            return LandingErrors.SectionNotFound;

        try
        {
            if (section.Status == LandingSectionStatus.Draft)
            {
                section.UpdateDraft(
                    request.ArabicTitle,
                    request.EnglishTitle,
                    request.ArabicBody,
                    request.EnglishBody,
                    request.ContentJson,
                    request.ImagePath,
                    request.DisplayOrder,
                    request.IsVisible);
                await db.SaveChangesAsync(cancellationToken);
                return section.ToAdminResponse(
                    await LandingAdminWarnings.CountAsync(db, familiesDb, caregiversDb, section, cancellationToken));
            }

            if (section.Status == LandingSectionStatus.Archived)
                return LandingErrors.SectionInvalid;

            LandingSection? twin = await db.LandingSections
                .SingleOrDefaultAsync(
                    item => item.SourceId == section.Id &&
                        item.Status == LandingSectionStatus.Draft,
                    cancellationToken);
            if (twin is null)
            {
                twin = LandingSection.Create(
                    section.Type,
                    request.ArabicTitle,
                    request.EnglishTitle,
                    request.ArabicBody,
                    request.EnglishBody,
                    request.ContentJson,
                    request.ImagePath,
                    request.DisplayOrder,
                    request.IsVisible,
                    section.Id,
                    DateTime.UtcNow);
                db.LandingSections.Add(twin);
            }
            else
            {
                twin.UpdateDraft(
                    request.ArabicTitle,
                    request.EnglishTitle,
                    request.ArabicBody,
                    request.EnglishBody,
                    request.ContentJson,
                    request.ImagePath,
                    request.DisplayOrder,
                    request.IsVisible);
            }

            await db.SaveChangesAsync(cancellationToken);
            return twin.ToAdminResponse(
                await LandingAdminWarnings.CountAsync(db, familiesDb, caregiversDb, twin, cancellationToken));
        }
        catch (DomainException)
        {
            return LandingErrors.SectionInvalid;
        }
    }
}

public sealed record PublishLandingSectionCommand(Guid Id)
    : ICommand<LandingAdminSectionResponse>;

public sealed class PublishLandingSectionCommandHandler(
    ICmsDbContext db,
    IFamiliesDbContext familiesDb,
    ICaregiversDbContext caregiversDb)
    : ICommandHandler<PublishLandingSectionCommand, LandingAdminSectionResponse>
{
    public async Task<Result<LandingAdminSectionResponse>> Handle(
        PublishLandingSectionCommand request, CancellationToken cancellationToken)
    {
        LandingSection? section = await db.LandingSections
            .SingleOrDefaultAsync(
                item => item.Id == new LandingSectionId(request.Id), cancellationToken);
        if (section is null)
            return LandingErrors.SectionNotFound;
        if (section.Status != LandingSectionStatus.Draft)
            return LandingErrors.SectionInvalid;
        if (section.Type == LandingSectionType.Hero && string.IsNullOrWhiteSpace(section.ImagePath))
            return LandingErrors.Incomplete("Hero section requires a cover image.");

        try
        {
            section.Publish();
        }
        catch (DomainException ex)
        {
            return LandingErrors.Incomplete(ex.Message);
        }

        List<LandingSection> superseded = await db.LandingSections
            .Where(item => item.Type == section.Type &&
                item.Status == LandingSectionStatus.Published &&
                item.Id != section.Id)
            .ToListAsync(cancellationToken);
        foreach (LandingSection previous in superseded)
            previous.Archive();

        await db.SaveChangesAsync(cancellationToken);

        return section.ToAdminResponse(
            await LandingAdminWarnings.CountAsync(db, familiesDb, caregiversDb, section, cancellationToken));
    }
}

public sealed record SetLandingSectionVisibilityCommand(Guid Id, bool IsVisible)
    : ICommand;

public sealed class SetLandingSectionVisibilityCommandHandler(ICmsDbContext db)
    : ICommandHandler<SetLandingSectionVisibilityCommand>
{
    public async Task<Result> Handle(
        SetLandingSectionVisibilityCommand request, CancellationToken cancellationToken)
    {
        LandingSection? section = await db.LandingSections
            .SingleOrDefaultAsync(
                item => item.Id == new LandingSectionId(request.Id), cancellationToken);
        if (section is null)
            return Result.Failure(LandingErrors.SectionNotFound);

        section.SetVisibility(request.IsVisible);
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

public sealed record DeleteLandingSectionCommand(Guid Id)
    : ICommand;

public sealed class DeleteLandingSectionCommandHandler(ICmsDbContext db)
    : ICommandHandler<DeleteLandingSectionCommand>
{
    public async Task<Result> Handle(
        DeleteLandingSectionCommand request, CancellationToken cancellationToken)
    {
        LandingSection? section = await db.LandingSections
            .SingleOrDefaultAsync(
                item => item.Id == new LandingSectionId(request.Id), cancellationToken);
        if (section is null)
            return Result.Failure(LandingErrors.SectionNotFound);
        if (section.Status != LandingSectionStatus.Draft)
            return Result.Failure(LandingErrors.PublishedHistoryConflict);

        db.LandingSections.Remove(section);
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

public sealed record LandingReorderItem(Guid Id, int DisplayOrder);

public sealed record ReorderLandingSectionsCommand(IReadOnlyList<LandingReorderItem> Items)
    : ICommand;

public sealed class ReorderLandingSectionsCommandHandler(ICmsDbContext db)
    : ICommandHandler<ReorderLandingSectionsCommand>
{
    public async Task<Result> Handle(
        ReorderLandingSectionsCommand request, CancellationToken cancellationToken)
    {
        if (request.Items.Count == 0 ||
            request.Items.Any(item => item.Id == Guid.Empty || item.DisplayOrder < 0) ||
            request.Items.Select(item => item.Id).Distinct().Count() != request.Items.Count)
            return Result.Failure(LandingErrors.ReorderInvalid);

        List<LandingSection> sections = await db.LandingSections.ToListAsync(cancellationToken);
        Dictionary<Guid, LandingSection> byId = sections.ToDictionary(section => section.Id.Value);

        if (request.Items.Any(item => !byId.ContainsKey(item.Id)))
            return Result.Failure(LandingErrors.ReorderInvalid);

        foreach (LandingReorderItem item in request.Items)
            byId[item.Id].SetDisplayOrder(item.DisplayOrder);

        await db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

internal static class LandingAdminWarnings
{
    public static async Task<int> CountAsync(
        ICmsDbContext db,
        IFamiliesDbContext familiesDb,
        ICaregiversDbContext caregiversDb,
        LandingSection section,
        CancellationToken cancellationToken)
    {
        Dictionary<Guid, ResolvedSectionRefs> resolved = await LandingRefResolver.ResolveAsync(
            db, familiesDb, caregiversDb, [section], cancellationToken);
        return resolved.TryGetValue(section.Id.Value, out ResolvedSectionRefs? refs)
            ? refs.DanglingRefCount
            : 0;
    }
}

