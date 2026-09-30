using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.Modules.Cms.Application.Abstractions.Data;
using Sanad.Modules.Cms.Domain.Welcome;

namespace Sanad.Modules.Cms.Application.Welcome;

public sealed record ElderlyWelcomeBenefitInput(int DisplayOrder, string ArabicTitle, string EnglishTitle, string ArabicDescription, string EnglishDescription);
public sealed record ElderlyWelcomeBenefitResponse(int DisplayOrder, string ArabicTitle, string EnglishTitle, string ArabicDescription, string EnglishDescription);
public sealed record ElderlyWelcomeResponse(Guid Id, string ArabicHeadline, string EnglishHeadline, string ArabicCtaLabel, string EnglishCtaLabel, string CtaAction, ElderlyWelcomePublicationStatus Status, DateTime CreatedOnUtc, DateTime UpdatedOnUtc, DateTime? PublishedOnUtc, IReadOnlyList<ElderlyWelcomeBenefitResponse> Benefits);
public sealed record LocalizedElderlyWelcomeResponse(string Language, string Headline, IReadOnlyList<LocalizedElderlyWelcomeBenefit> Benefits, string CtaLabel, string CtaAction);
public sealed record LocalizedElderlyWelcomeBenefit(int DisplayOrder, string Title, string Description);
public sealed record GetPublishedElderlyWelcomeQuery(string Language) : IQuery<LocalizedElderlyWelcomeResponse>;
public sealed record GetElderlyWelcomeQuery : IQuery<ElderlyWelcomeResponse>;
public sealed record CreateElderlyWelcomeCommand(string ArabicHeadline, string EnglishHeadline, string ArabicCtaLabel, string EnglishCtaLabel, IReadOnlyList<ElderlyWelcomeBenefitInput> Benefits) : ICommand<ElderlyWelcomeResponse>;
public sealed record UpdateElderlyWelcomeCommand(string ArabicHeadline, string EnglishHeadline, string ArabicCtaLabel, string EnglishCtaLabel, IReadOnlyList<ElderlyWelcomeBenefitInput> Benefits) : ICommand<ElderlyWelcomeResponse>;
public sealed record PublishElderlyWelcomeCommand : ICommand<ElderlyWelcomeResponse>;
public sealed record UnpublishElderlyWelcomeCommand : ICommand<ElderlyWelcomeResponse>;

public static class ElderlyWelcomeErrors
{
    public static readonly Error NotFound = new("Cms.ElderlyWelcome.NotFound", "The Elderly welcome content is not configured.");
    public static readonly Error NotPublished = new("Cms.ElderlyWelcome.NotPublished", "The Elderly welcome content is not published.");
    public static readonly Error AlreadyExists = new("Cms.ElderlyWelcome.AlreadyExists", "The Elderly welcome content already exists.");
    public static readonly Error Invalid = new("Cms.ElderlyWelcome.Invalid", "The Elderly welcome content is invalid for this operation.");
    public static readonly Error InvalidState = new("Cms.ElderlyWelcome.InvalidState", "The Elderly welcome cannot make that lifecycle transition.");
}

internal static class ElderlyWelcomeMapping
{
    public static ElderlyWelcomeResponse Map(ElderlyWelcome w) => new(w.Id, w.ArabicHeadline, w.EnglishHeadline, w.ArabicCtaLabel, w.EnglishCtaLabel, w.CtaAction, w.Status, w.CreatedOnUtc, w.UpdatedOnUtc, w.PublishedOnUtc, w.Benefits.OrderBy(x => x.DisplayOrder).Select(x => new ElderlyWelcomeBenefitResponse(x.DisplayOrder, x.ArabicTitle, x.EnglishTitle, x.ArabicDescription, x.EnglishDescription)).ToArray());
    public static IReadOnlyList<ElderlyWelcomeBenefitDraft> Drafts(IReadOnlyList<ElderlyWelcomeBenefitInput> input) => input.Select(x => new ElderlyWelcomeBenefitDraft(x.DisplayOrder, x.ArabicTitle, x.EnglishTitle, x.ArabicDescription, x.EnglishDescription)).ToArray();
}

public sealed class GetPublishedElderlyWelcomeQueryHandler(ICmsDbContext db) : IQueryHandler<GetPublishedElderlyWelcomeQuery, LocalizedElderlyWelcomeResponse>
{
    public async Task<Result<LocalizedElderlyWelcomeResponse>> Handle(GetPublishedElderlyWelcomeQuery r, CancellationToken ct)
    {
        var welcome = await db.ElderlyWelcomes.AsNoTracking().Include(x => x.Benefits).SingleOrDefaultAsync(x => x.Id == ElderlyWelcome.SingletonId && x.Status == ElderlyWelcomePublicationStatus.Published, ct);
        if (welcome is null) return ElderlyWelcomeErrors.NotPublished;
        var arabic = r.Language.Equals("ar", StringComparison.OrdinalIgnoreCase);
        return new LocalizedElderlyWelcomeResponse(arabic ? "ar" : "en", arabic ? welcome.ArabicHeadline : welcome.EnglishHeadline,
            welcome.Benefits.OrderBy(x => x.DisplayOrder).Select(x => new LocalizedElderlyWelcomeBenefit(x.DisplayOrder, arabic ? x.ArabicTitle : x.EnglishTitle, arabic ? x.ArabicDescription : x.EnglishDescription)).ToArray(),
            arabic ? welcome.ArabicCtaLabel : welcome.EnglishCtaLabel, welcome.CtaAction);
    }
}
public sealed class GetElderlyWelcomeQueryHandler(ICmsDbContext db) : IQueryHandler<GetElderlyWelcomeQuery, ElderlyWelcomeResponse>
{
    public async Task<Result<ElderlyWelcomeResponse>> Handle(GetElderlyWelcomeQuery r, CancellationToken ct)
    { var w = await db.ElderlyWelcomes.AsNoTracking().Include(x => x.Benefits).SingleOrDefaultAsync(x => x.Id == ElderlyWelcome.SingletonId, ct); return w is null ? ElderlyWelcomeErrors.NotFound : ElderlyWelcomeMapping.Map(w); }
}
public sealed class CreateElderlyWelcomeCommandHandler(ICmsDbContext db) : ICommandHandler<CreateElderlyWelcomeCommand, ElderlyWelcomeResponse>
{
    public async Task<Result<ElderlyWelcomeResponse>> Handle(CreateElderlyWelcomeCommand r, CancellationToken ct)
    {
        if (await db.ElderlyWelcomes.AnyAsync(ct)) return ElderlyWelcomeErrors.AlreadyExists;
        try
        {
            var w = ElderlyWelcome.Create(r.ArabicHeadline, r.EnglishHeadline, r.ArabicCtaLabel, r.EnglishCtaLabel, ElderlyWelcomeMapping.Drafts(r.Benefits));
            db.ElderlyWelcomes.Add(w);
            await db.SaveChangesAsync(ct);
            return ElderlyWelcomeMapping.Map(w);
        }
        catch (DomainException) { return ElderlyWelcomeErrors.Invalid; }
        catch (DbUpdateException)
        {
            // A concurrent request may win the fixed singleton primary key after AnyAsync.
            // Translate only when the singleton now exists; preserve unrelated persistence errors.
            if (await db.ElderlyWelcomes.AsNoTracking().AnyAsync(x => x.Id == ElderlyWelcome.SingletonId, ct))
            {
                return ElderlyWelcomeErrors.AlreadyExists;
            }

            throw;
        }
    }
}
public sealed class UpdateElderlyWelcomeCommandHandler(ICmsDbContext db) : ICommandHandler<UpdateElderlyWelcomeCommand, ElderlyWelcomeResponse>
{
    public async Task<Result<ElderlyWelcomeResponse>> Handle(UpdateElderlyWelcomeCommand r, CancellationToken ct)
    {
        var w = await db.ElderlyWelcomes.Include(x => x.Benefits).SingleOrDefaultAsync(x => x.Id == ElderlyWelcome.SingletonId, ct); if (w is null) return ElderlyWelcomeErrors.NotFound;
        if (w.Status != ElderlyWelcomePublicationStatus.Draft) return ElderlyWelcomeErrors.InvalidState;
        try { w.Update(r.ArabicHeadline, r.EnglishHeadline, r.ArabicCtaLabel, r.EnglishCtaLabel, ElderlyWelcomeMapping.Drafts(r.Benefits)); await db.SaveChangesAsync(ct); return ElderlyWelcomeMapping.Map(w); } catch (DomainException) { return ElderlyWelcomeErrors.Invalid; }
    }
}
public sealed class PublishElderlyWelcomeCommandHandler(ICmsDbContext db) : ICommandHandler<PublishElderlyWelcomeCommand, ElderlyWelcomeResponse>
{
    public async Task<Result<ElderlyWelcomeResponse>> Handle(PublishElderlyWelcomeCommand r, CancellationToken ct)
    { var w = await db.ElderlyWelcomes.Include(x => x.Benefits).SingleOrDefaultAsync(x => x.Id == ElderlyWelcome.SingletonId, ct); if (w is null) return ElderlyWelcomeErrors.NotFound; if (w.Status == ElderlyWelcomePublicationStatus.Archived) return ElderlyWelcomeErrors.InvalidState; try { w.Publish(); await db.SaveChangesAsync(ct); return ElderlyWelcomeMapping.Map(w); } catch (DomainException) { return ElderlyWelcomeErrors.Invalid; } }
}
public sealed class UnpublishElderlyWelcomeCommandHandler(ICmsDbContext db) : ICommandHandler<UnpublishElderlyWelcomeCommand, ElderlyWelcomeResponse>
{
    public async Task<Result<ElderlyWelcomeResponse>> Handle(UnpublishElderlyWelcomeCommand r, CancellationToken ct)
    { var w = await db.ElderlyWelcomes.Include(x => x.Benefits).SingleOrDefaultAsync(x => x.Id == ElderlyWelcome.SingletonId, ct); if (w is null) return ElderlyWelcomeErrors.NotFound; if (w.Status == ElderlyWelcomePublicationStatus.Archived) return ElderlyWelcomeErrors.InvalidState; try { w.Unpublish(); await db.SaveChangesAsync(ct); return ElderlyWelcomeMapping.Map(w); } catch (DomainException) { return ElderlyWelcomeErrors.Invalid; } }
}
