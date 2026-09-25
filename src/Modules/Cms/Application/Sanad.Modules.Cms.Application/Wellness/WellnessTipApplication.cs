using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.Modules.Cms.Application.Abstractions.Data;
using Sanad.Modules.Cms.Domain.Wellness;

namespace Sanad.Modules.Cms.Application.Wellness;

public sealed record WellnessTipSectionInput(int DisplayOrder, string ArabicText, string EnglishText)
{
    public WellnessTipSectionDraft ToDraft() => new(DisplayOrder, ArabicText, EnglishText);
}
public sealed record WellnessTipResponse(Guid Id, string ArabicTitle, string EnglishTitle, string Category, string ImagePath, WellnessTipPublicationStatus Status, DateTime CreatedOnUtc, DateTime UpdatedOnUtc, DateTime? PublishedOnUtc, IReadOnlyList<WellnessTipSectionResponse> Sections);
public sealed record WellnessTipSectionResponse(int DisplayOrder, string ArabicText, string EnglishText);

public static class WellnessTipErrors
{
    public static readonly Error NotFound = new("Cms.WellnessTip.NotFound", "Wellness tip was not found.");
    public static readonly Error NotPublished = new("Cms.WellnessTip.NotPublished", "No published wellness tip exists.");
    public static readonly Error InvalidOperation = new("Cms.WellnessTip.InvalidOperation", "The requested wellness tip operation is invalid.");
}

public static class WellnessTipMappings
{
    public static WellnessTipResponse ToResponse(this WellnessTip tip) => new(tip.Id, tip.ArabicTitle, tip.EnglishTitle, tip.Category, tip.ImagePath, tip.Status, tip.CreatedOnUtc, tip.UpdatedOnUtc, tip.PublishedOnUtc, tip.Sections.OrderBy(x => x.DisplayOrder).Select(x => new WellnessTipSectionResponse(x.DisplayOrder, x.ArabicText, x.EnglishText)).ToList());
}

public sealed record CreateWellnessTipCommand(string ArabicTitle, string EnglishTitle, string Category, string ImagePath, IReadOnlyList<WellnessTipSectionInput> Sections) : ICommand<WellnessTipResponse>;
public sealed record UpdateWellnessTipCommand(Guid Id, string ArabicTitle, string EnglishTitle, string Category, string? ImagePath, IReadOnlyList<WellnessTipSectionInput>? Sections) : ICommand<WellnessTipResponse>;
public sealed record PublishWellnessTipCommand(Guid Id) : ICommand<WellnessTipResponse>;
public sealed record ArchiveWellnessTipCommand(Guid Id) : ICommand<WellnessTipResponse>;
public sealed record GetWellnessTipQuery(Guid Id, bool PublishedOnly = false) : IQuery<WellnessTipResponse>;
public sealed record ListWellnessTipsQuery(WellnessTipPublicationStatus? Status, string? Category, string? Search, bool PublishedOnly = false, int Page = 1, int PageSize = 20) : IQuery<WellnessTipPageResponse>;
public sealed record WellnessTipPageResponse(IReadOnlyList<WellnessTipResponse> Items, int Page, int PageSize, int TotalCount);

internal static class WellnessTipCommandHelpers
{
    public static IReadOnlyList<WellnessTipSectionDraft> Drafts(IEnumerable<WellnessTipSectionInput>? sections) => (sections ?? []).Select(x => x.ToDraft()).ToList();
    public static async Task<WellnessTip?> Find(ICmsDbContext db, Guid id, CancellationToken ct) => await db.WellnessTips.Include(x => x.Sections).SingleOrDefaultAsync(x => x.Id == id, ct);
}

public sealed class CreateWellnessTipCommandHandler(ICmsDbContext db) : ICommandHandler<CreateWellnessTipCommand, WellnessTipResponse>
{
    public async Task<Result<WellnessTipResponse>> Handle(CreateWellnessTipCommand r, CancellationToken ct)
    { try { var tip = WellnessTip.Create(r.ArabicTitle, r.EnglishTitle, r.Category, r.ImagePath, WellnessTipCommandHelpers.Drafts(r.Sections)); db.WellnessTips.Add(tip); await db.SaveChangesAsync(ct); return tip.ToResponse(); } catch (DomainException) { return WellnessTipErrors.InvalidOperation; } }
}
public sealed class UpdateWellnessTipCommandHandler(ICmsDbContext db) : ICommandHandler<UpdateWellnessTipCommand, WellnessTipResponse>
{
    public async Task<Result<WellnessTipResponse>> Handle(UpdateWellnessTipCommand r, CancellationToken ct)
    { var tip = await WellnessTipCommandHelpers.Find(db, r.Id, ct); if (tip is null) return WellnessTipErrors.NotFound; try { tip.Update(r.ArabicTitle, r.EnglishTitle, r.Category, r.ImagePath ?? tip.ImagePath, r.Sections is null ? tip.Sections.Select(x => new WellnessTipSectionDraft(x.DisplayOrder, x.ArabicText, x.EnglishText)).ToList() : WellnessTipCommandHelpers.Drafts(r.Sections)); await db.SaveChangesAsync(ct); return tip.ToResponse(); } catch (DomainException) { return WellnessTipErrors.InvalidOperation; } }
}
public sealed class PublishWellnessTipCommandHandler(ICmsDbContext db) : ICommandHandler<PublishWellnessTipCommand, WellnessTipResponse>
{
    public async Task<Result<WellnessTipResponse>> Handle(PublishWellnessTipCommand r, CancellationToken ct) { var tip = await WellnessTipCommandHelpers.Find(db, r.Id, ct); if (tip is null) return WellnessTipErrors.NotFound; try { tip.Publish(); await db.SaveChangesAsync(ct); return tip.ToResponse(); } catch (DomainException) { return WellnessTipErrors.InvalidOperation; } }
}
public sealed class ArchiveWellnessTipCommandHandler(ICmsDbContext db) : ICommandHandler<ArchiveWellnessTipCommand, WellnessTipResponse>
{
    public async Task<Result<WellnessTipResponse>> Handle(ArchiveWellnessTipCommand r, CancellationToken ct) { var tip = await WellnessTipCommandHelpers.Find(db, r.Id, ct); if (tip is null) return WellnessTipErrors.NotFound; tip.Archive(); await db.SaveChangesAsync(ct); return tip.ToResponse(); }
}
public sealed class GetWellnessTipQueryHandler(ICmsDbContext db) : IQueryHandler<GetWellnessTipQuery, WellnessTipResponse>
{
    public async Task<Result<WellnessTipResponse>> Handle(GetWellnessTipQuery r, CancellationToken ct) { var q = db.WellnessTips.AsNoTracking().Include(x => x.Sections).Where(x => x.Id == r.Id); if (r.PublishedOnly) q = q.Where(x => x.Status == WellnessTipPublicationStatus.Published); var tip = await q.SingleOrDefaultAsync(ct); return tip is null ? (r.PublishedOnly ? WellnessTipErrors.NotPublished : WellnessTipErrors.NotFound) : tip.ToResponse(); }
}
public sealed class ListWellnessTipsQueryHandler(ICmsDbContext db) : IQueryHandler<ListWellnessTipsQuery, WellnessTipPageResponse>
{
    public async Task<Result<WellnessTipPageResponse>> Handle(ListWellnessTipsQuery r, CancellationToken ct)
    {
        var page = Math.Max(1, r.Page);
        var pageSize = Math.Clamp(r.PageSize, 1, 100);
        var q = db.WellnessTips.AsNoTracking().AsQueryable();
        if (r.PublishedOnly) q = q.Where(x => x.Status == WellnessTipPublicationStatus.Published);
        if (r.Status is not null) q = q.Where(x => x.Status == r.Status);
        if (!string.IsNullOrWhiteSpace(r.Category)) q = q.Where(x => x.Category == r.Category);
        if (!string.IsNullOrWhiteSpace(r.Search)) q = q.Where(x => x.ArabicTitle.Contains(r.Search) || x.EnglishTitle.Contains(r.Search));
        var totalCount = await q.CountAsync(ct);
        var offset = ((long)page - 1) * pageSize;
        if (offset > int.MaxValue) return new WellnessTipPageResponse([], page, pageSize, totalCount);
        var tips = await q.OrderByDescending(x => x.PublishedOnUtc ?? x.UpdatedOnUtc).ThenBy(x => x.Id)
            .Skip((int)offset).Take(pageSize).Include(x => x.Sections).ToListAsync(ct);
        return new WellnessTipPageResponse(tips.Select(x => x.ToResponse()).ToList(), page, pageSize, totalCount);
    }
}
