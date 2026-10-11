using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.Modules.Cms.Application.Abstractions.Data;
using Sanad.Modules.Cms.Domain.Library;

namespace Sanad.Modules.Cms.Application.Library;

public sealed record LibraryContentSectionInput(
    int DisplayOrder,
    string ArabicTitle,
    string EnglishTitle,
    IReadOnlyList<string>? ArabicBullets,
    IReadOnlyList<string>? EnglishBullets)
{
    public LibraryContentSectionDraft ToDraft() => new(
        DisplayOrder,
        ArabicTitle,
        EnglishTitle,
        ArabicBullets ?? [],
        EnglishBullets ?? []);
}

public sealed record CreateLibraryContentCommand(
    LibraryContentType Type,
    Guid? CategoryId,
    string ArabicTitle,
    string EnglishTitle,
    string ArabicSummary,
    string EnglishSummary,
    string? VideoUrl,
    int? DurationSeconds,
    string CoverImagePath,
    bool IsPremium,
    IReadOnlyList<LibraryContentSectionInput>? Sections)
    : ICommand<LibraryAdminContentResponse>;

public sealed class CreateLibraryContentCommandHandler(ICmsDbContext db)
    : ICommandHandler<CreateLibraryContentCommand, LibraryAdminContentResponse>
{
    public async Task<Result<LibraryAdminContentResponse>> Handle(
        CreateLibraryContentCommand request, CancellationToken cancellationToken)
    {
        (Error? categoryError, LibraryCategoryId? categoryId) =
            await LibraryCategoryLookup.ValidateAsync(db, request.CategoryId, cancellationToken);
        if (categoryError is not null)
            return categoryError;

        try
        {
            LibraryContent content = LibraryContent.Create(
                request.Type,
                categoryId,
                request.ArabicTitle,
                request.EnglishTitle,
                request.ArabicSummary,
                request.EnglishSummary,
                request.VideoUrl,
                request.DurationSeconds,
                request.CoverImagePath,
                request.IsPremium,
                request.Sections?.Select(section => section.ToDraft()).ToList() ?? [],
                DateTime.UtcNow);

            db.LibraryContents.Add(content);
            await db.SaveChangesAsync(cancellationToken);

            return content.ToAdminResponse();
        }
        catch (DomainException)
        {
            return LibraryErrors.ContentInvalid;
        }
    }
}

public sealed record UpdateLibraryContentCommand(
    Guid Id,
    Guid? CategoryId,
    string ArabicTitle,
    string EnglishTitle,
    string ArabicSummary,
    string EnglishSummary,
    string? VideoUrl,
    int? DurationSeconds,
    string? CoverImagePath,
    bool IsPremium,
    IReadOnlyList<LibraryContentSectionInput>? Sections)
    : ICommand<LibraryAdminContentResponse>;

public sealed class UpdateLibraryContentCommandHandler(ICmsDbContext db)
    : ICommandHandler<UpdateLibraryContentCommand, LibraryAdminContentResponse>
{
    public async Task<Result<LibraryAdminContentResponse>> Handle(
        UpdateLibraryContentCommand request, CancellationToken cancellationToken)
    {
        LibraryContent? content = await db.LibraryContents
            .SingleOrDefaultAsync(
                item => item.Id == new LibraryContentId(request.Id), cancellationToken);
        if (content is null)
            return LibraryErrors.ContentNotFound;
        if (content.Status != LibraryContentStatus.Draft)
            return LibraryErrors.ContentInvalidOperation;

        (Error? categoryError, LibraryCategoryId? categoryId) =
            await LibraryCategoryLookup.ValidateAsync(db, request.CategoryId, cancellationToken);
        if (categoryError is not null)
            return categoryError;

        IReadOnlyList<LibraryContentSectionDraft> sections = request.Sections is null
            ? content.Sections
                .Select(section => new LibraryContentSectionDraft(
                    section.DisplayOrder,
                    section.ArabicTitle,
                    section.EnglishTitle,
                    section.ArabicBullets,
                    section.EnglishBullets))
                .ToList()
            : request.Sections.Select(section => section.ToDraft()).ToList();

        try
        {
            content.Update(
                categoryId,
                request.ArabicTitle,
                request.EnglishTitle,
                request.ArabicSummary,
                request.EnglishSummary,
                request.VideoUrl,
                request.DurationSeconds,
                string.IsNullOrWhiteSpace(request.CoverImagePath)
                    ? content.CoverImagePath
                    : request.CoverImagePath,
                request.IsPremium,
                sections);
        }
        catch (DomainException)
        {
            return LibraryErrors.ContentInvalid;
        }

        await db.SaveChangesAsync(cancellationToken);

        return content.ToAdminResponse();
    }
}

public sealed record PublishLibraryContentCommand(Guid Id)
    : ICommand<LibraryAdminContentResponse>;

public sealed class PublishLibraryContentCommandHandler(ICmsDbContext db)
    : ICommandHandler<PublishLibraryContentCommand, LibraryAdminContentResponse>
{
    public async Task<Result<LibraryAdminContentResponse>> Handle(
        PublishLibraryContentCommand request, CancellationToken cancellationToken)
    {
        LibraryContent? content = await db.LibraryContents
            .SingleOrDefaultAsync(
                item => item.Id == new LibraryContentId(request.Id), cancellationToken);
        if (content is null)
            return LibraryErrors.ContentNotFound;

        try
        {
            content.Publish();
        }
        catch (DomainException)
        {
            return LibraryErrors.ContentInvalidOperation;
        }

        await db.SaveChangesAsync(cancellationToken);

        return content.ToAdminResponse();
    }
}

public sealed record ArchiveLibraryContentCommand(Guid Id)
    : ICommand<LibraryAdminContentResponse>;

public sealed class ArchiveLibraryContentCommandHandler(ICmsDbContext db)
    : ICommandHandler<ArchiveLibraryContentCommand, LibraryAdminContentResponse>
{
    public async Task<Result<LibraryAdminContentResponse>> Handle(
        ArchiveLibraryContentCommand request, CancellationToken cancellationToken)
    {
        LibraryContent? content = await db.LibraryContents
            .SingleOrDefaultAsync(
                item => item.Id == new LibraryContentId(request.Id), cancellationToken);
        if (content is null)
            return LibraryErrors.ContentNotFound;

        content.Archive();
        await db.SaveChangesAsync(cancellationToken);

        return content.ToAdminResponse();
    }
}
