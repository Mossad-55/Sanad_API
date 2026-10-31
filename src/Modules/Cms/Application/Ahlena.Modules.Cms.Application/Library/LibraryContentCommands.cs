using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Exceptions;
using Ahlena.Modules.Cms.Application.Abstractions.Data;
using Ahlena.Modules.Cms.Domain.Library;

namespace Ahlena.Modules.Cms.Application.Library;

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
                // Creation never carries bytes: an approved upload is attached
                // through the dedicated video endpoint, which stores the file
                // before the reference is persisted.
                videoStorageKey: null,
                request.DurationSeconds,
                request.CoverImagePath,
                request.IsPremium,
                request.Sections?.Select(section => section.ToDraft()).ToList() ?? [],
                DateTime.UtcNow);

            db.LibraryContents.Add(content);
            await db.SaveChangesAsync(cancellationToken);

            return content.ToAdminResponse();
        }
        catch (DomainException exception)
        {
            return LibraryErrors.ContentInvalidWith(
                LibraryContentFieldDetail.FromDomainException(
                    exception.Message,
                    request.Type,
                    request.Sections));
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
        catch (DomainException exception)
        {
            IReadOnlyList<LibraryContentSectionInput> effectiveSections = sections
                .Select(section => new LibraryContentSectionInput(
                    section.DisplayOrder,
                    section.ArabicTitle,
                    section.EnglishTitle,
                    section.ArabicBullets,
                    section.EnglishBullets))
                .ToList();

            return LibraryErrors.ContentInvalidWith(
                LibraryContentFieldDetail.FromDomainException(
                    exception.Message,
                    content.Type,
                    effectiveSections));
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

public sealed record RestoreLibraryContentCommand(Guid Id)
    : ICommand<LibraryAdminContentResponse>;

/// <summary>
/// Restores archived content to Draft (locked decision #24). It is not a
/// publication: the restored row must pass the normal draft update validation
/// before it can be published again. A non-archived target is the pinned
/// lifecycle violation <c>Cms.LibraryContent.InvalidOperation</c> (409), the
/// same code the publish-on-archived guard uses.
/// </summary>
public sealed class RestoreLibraryContentCommandHandler(ICmsDbContext db)
    : ICommandHandler<RestoreLibraryContentCommand, LibraryAdminContentResponse>
{
    public async Task<Result<LibraryAdminContentResponse>> Handle(
        RestoreLibraryContentCommand request, CancellationToken cancellationToken)
    {
        LibraryContent? content = await db.LibraryContents
            .SingleOrDefaultAsync(
                item => item.Id == new LibraryContentId(request.Id), cancellationToken);
        if (content is null)
            return LibraryErrors.ContentNotFound;

        try
        {
            content.Restore();
        }
        catch (DomainException)
        {
            return LibraryErrors.ContentInvalidOperation;
        }

        await db.SaveChangesAsync(cancellationToken);

        return content.ToAdminResponse();
    }
}

/// <summary>
/// Attaches (or replaces) the private storage reference of an uploaded video.
/// The bytes are already persisted when this runs; a failure here leaves the
/// caller responsible for deleting the fresh object. Archived content is the
/// pinned lifecycle violation <c>Cms.LibraryContent.InvalidOperation</c> (409);
/// a non-video target or an unsafe reference is
/// <c>Cms.LibraryContent.Invalid</c> (400) with the <c>videoStorageKey</c> field.
/// </summary>
public sealed record AttachLibraryContentVideoCommand(Guid Id, string StorageKey)
    : ICommand<LibraryAdminContentResponse>;

public sealed class AttachLibraryContentVideoCommandHandler(ICmsDbContext db)
    : ICommandHandler<AttachLibraryContentVideoCommand, LibraryAdminContentResponse>
{
    public async Task<Result<LibraryAdminContentResponse>> Handle(
        AttachLibraryContentVideoCommand request, CancellationToken cancellationToken)
    {
        LibraryContent? content = await db.LibraryContents
            .SingleOrDefaultAsync(
                item => item.Id == new LibraryContentId(request.Id), cancellationToken);
        if (content is null)
            return LibraryErrors.ContentNotFound;
        if (content.Status == LibraryContentStatus.Archived)
            return LibraryErrors.ContentInvalidOperation;

        try
        {
            content.AttachVideo(request.StorageKey);
        }
        catch (DomainException exception)
        {
            return LibraryErrors.ContentInvalidWith(
                LibraryContentFieldDetail.FromDomainException(
                    exception.Message, content.Type, null));
        }

        await db.SaveChangesAsync(cancellationToken);

        return content.ToAdminResponse();
    }
}

/// <summary>
/// Detaches the stored video reference. The change is persisted before the
/// caller deletes the physical object, so a storage failure can only leave an
/// unreferenced file — never a reference to bytes that are already gone.
/// Archived content is 409; a non-video target is 400 with the
/// <c>videoStorageKey</c> field. Content without bytes is a no-op (200).
/// </summary>
public sealed record RemoveLibraryContentVideoCommand(Guid Id)
    : ICommand<LibraryAdminContentResponse>;

public sealed class RemoveLibraryContentVideoCommandHandler(ICmsDbContext db)
    : ICommandHandler<RemoveLibraryContentVideoCommand, LibraryAdminContentResponse>
{
    public async Task<Result<LibraryAdminContentResponse>> Handle(
        RemoveLibraryContentVideoCommand request, CancellationToken cancellationToken)
    {
        LibraryContent? content = await db.LibraryContents
            .SingleOrDefaultAsync(
                item => item.Id == new LibraryContentId(request.Id), cancellationToken);
        if (content is null)
            return LibraryErrors.ContentNotFound;
        if (content.Status == LibraryContentStatus.Archived)
            return LibraryErrors.ContentInvalidOperation;

        try
        {
            content.RemoveVideo();
        }
        catch (DomainException exception)
        {
            return LibraryErrors.ContentInvalidWith(
                LibraryContentFieldDetail.FromDomainException(
                    exception.Message, content.Type, null));
        }

        await db.SaveChangesAsync(cancellationToken);

        return content.ToAdminResponse();
    }
}
