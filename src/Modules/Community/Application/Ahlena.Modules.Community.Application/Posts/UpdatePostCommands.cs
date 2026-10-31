using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Community.Application.Abstractions.Data;
using Ahlena.Modules.Community.Application.Categories;
using Ahlena.Modules.Community.Application.Uploads;
using Ahlena.Modules.Community.Domain.Categories;
using Ahlena.Modules.Community.Domain.Posts;
using Ahlena.Modules.Community.Domain.Uploads;
using DomainPost = Ahlena.Modules.Community.Domain.Posts.Post;

namespace Ahlena.Modules.Community.Application.Posts;

/// <summary>
/// FUR post management failure codes. The not-found code reuses
/// <c>Community.PostNotFound</c> (the shared mapper's 404), the in-flight
/// conflict reuses <c>Community.InvalidModerationOperation</c> (the mapper's
/// 409) and the invalid image code reuses <c>Community.Image.Invalid</c> (the
/// mapper's 400) — no new wire contract, no shared-mapper amendment.
/// <para>
/// Status codes are pinned for these flows: an unknown post, a foreign
/// author's post and an already-deleted post all answer the same privacy 404
/// (the established Community convention — a caller cannot probe another
/// author's post state through the app endpoints); only the author's
/// non-deleted pending-review/published states are editable; delete is 204
/// once and 404 afterwards; a stale concurrent write is a 409 the client
/// resolves by re-reading.
/// </para>
/// </summary>
public static class CommunityPostLifecycleErrors
{
    public static readonly Error PostNotFound =
        new(
            "Community.PostNotFound",
            "Community post not found.");

    public static readonly Error PostNotEditable =
        new(
            "Community.InvalidModerationOperation",
            "The post can no longer be edited by its author.");

    public static readonly Error InvalidImageReference =
        new(
            "Community.Image.Invalid",
            "The post image must be a Community upload you are allowed to reference.");

    public static readonly Error InvalidPostFields =
        new(
            "Community.InvalidPostFields",
            "The post fields are invalid: both titles and both contents are required within their length limits.");

    private static readonly Error StaleLifecycleWrite =
        new(
            "Community.InvalidModerationOperation",
            "The post changed while the request was in flight; reload and retry.");

    public static Error StaleWrite => StaleLifecycleWrite;
}

/// <summary>
/// The authenticated author edits their own post. The acting author id is
/// server-derived from the authentication claim and passed by the controller;
/// no caller-supplied field can reach the ownership check, so app ownership
/// cannot be bypassed by a forged id. The author identity itself is immutable.
/// </summary>
public sealed record UpdatePostCommand(
    Guid PostId,
    Guid ActingAuthorId,
    string TitleArabic,
    string TitleEnglish,
    string ContentArabic,
    string ContentEnglish,
    string? ImageUrl,
    bool IsAnonymous,
    Guid? CategoryId) : ICommand<DomainPost>;

public sealed class UpdatePostCommandHandler(ICommunityDbContext dbContext)
    : ICommandHandler<UpdatePostCommand, DomainPost>
{
    public async Task<Result<DomainPost>> Handle(
        UpdatePostCommand request,
        CancellationToken cancellationToken)
    {
        DomainPost? post = await dbContext.Posts
            .SingleOrDefaultAsync(
                p => p.Id == new CommunityPostId(request.PostId),
                cancellationToken);

        // Unknown post, another author's post and a deleted post all answer
        // the same privacy not-found: no existence disclosure.
        if (post is null ||
            post.AuthorId != new UserId(request.ActingAuthorId) ||
            post.Status == PostStatus.Deleted)
        {
            return Result<DomainPost>.Failure(CommunityPostLifecycleErrors.PostNotFound);
        }

        if (await CommunityImageReferenceValidator.ValidateAsync(
                dbContext,
                post.ImageUrl,
                request.ImageUrl,
                request.ActingAuthorId,
                cancellationToken) is { IsFailure: true } imageResult)
        {
            return Result<DomainPost>.Failure(imageResult.Error);
        }

        CommunityCategoryId? categoryId = null;
        if (request.CategoryId is Guid rawCategoryId)
        {
            CommunityCategory? category = await dbContext.Categories
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    c => c.Id == new CommunityCategoryId(rawCategoryId),
                    cancellationToken);

            if (category is null)
                return Result<DomainPost>.Failure(CommunityCategoryErrors.InvalidCategory);
            if (!category.IsActive)
                return Result<DomainPost>.Failure(CommunityCategoryErrors.CategoryInactive);

            categoryId = category.Id;
        }

        if (post.Status is not (PostStatus.PendingReview or PostStatus.Published))
        {
            // Rejected/archived posts are not editable (409 via the shared
            // mapper family).
            return Result<DomainPost>.Failure(CommunityPostLifecycleErrors.PostNotEditable);
        }

        try
        {
            post.Update(
                request.TitleArabic,
                request.TitleEnglish,
                request.ContentArabic,
                request.ContentEnglish,
                request.ImageUrl,
                request.IsAnonymous,
                categoryId,
                DateTime.UtcNow);
        }
        catch (Ahlena.BuildingBlocks.Domain.Exceptions.DomainException)
        {
            // Only field validation (missing/oversized text) can still throw
            // here: the caller's own malformed input (400 default).
            return Result<DomainPost>.Failure(CommunityPostLifecycleErrors.InvalidPostFields);
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The lifecycle state moved underneath this edit (typically a
            // concurrent author/admin deletion). Never resurrect the post.
            if (dbContext is DbContext efContext)
            {
                efContext.ChangeTracker.Clear();
            }

            return Result<DomainPost>.Failure(CommunityPostLifecycleErrors.StaleWrite);
        }

        return Result<DomainPost>.Success(post);
    }
}

/// <summary>
/// The authenticated author deletes their own post. Ownership is derived from
/// authentication, enforced in the handler against the stored immutable
/// author id; a forged id cannot widen the scope. Deleted posts answer 404 to
/// their own author as well, so the second delete is not distinguishable.
/// </summary>
public sealed record DeletePostCommand(Guid PostId, Guid ActingAuthorId) : ICommand<bool>;

public sealed class DeletePostCommandHandler(ICommunityDbContext dbContext)
    : ICommandHandler<DeletePostCommand, bool>
{
    public async Task<Result<bool>> Handle(
        DeletePostCommand request,
        CancellationToken cancellationToken)
    {
        DomainPost? post = await dbContext.Posts
            .SingleOrDefaultAsync(
                p => p.Id == new CommunityPostId(request.PostId),
                cancellationToken);

        if (post is null ||
            post.AuthorId != new UserId(request.ActingAuthorId) ||
            post.Status == PostStatus.Deleted)
        {
            return Result<bool>.Failure(CommunityPostLifecycleErrors.PostNotFound);
        }

        post.Delete(new UserId(request.ActingAuthorId), DateTime.UtcNow);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The post moved (e.g. a moderation transition) concurrently;
            // the deletion is not lost, the client re-reads.
            if (dbContext is DbContext efContext)
            {
                efContext.ChangeTracker.Clear();
            }

            return Result<bool>.Failure(CommunityPostLifecycleErrors.StaleWrite);
        }

        return Result<bool>.Success(true);
    }
}

/// <summary>
/// Community moderation deletes any post, keeping the deletion audit. This is
/// deliberately the only Admin post write: the moderation permission grants no
/// edit or re-attribution of another author's content.
/// </summary>
public sealed record AdminDeletePostCommand(Guid PostId, UserId ModeratorId) : ICommand<bool>;

public sealed class AdminDeletePostCommandHandler(ICommunityDbContext dbContext)
    : ICommandHandler<AdminDeletePostCommand, bool>
{
    public async Task<Result<bool>> Handle(
        AdminDeletePostCommand request,
        CancellationToken cancellationToken)
    {
        DomainPost? post = await dbContext.Posts
            .SingleOrDefaultAsync(
                p => p.Id == new CommunityPostId(request.PostId),
                cancellationToken);

        // An unknown or already-deleted post answers the same 404.
        if (post is null || post.Status == PostStatus.Deleted)
        {
            return Result<bool>.Failure(CommunityPostLifecycleErrors.PostNotFound);
        }

        post.Delete(request.ModeratorId, DateTime.UtcNow);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            if (dbContext is DbContext efContext)
            {
                efContext.ChangeTracker.Clear();
            }

            return Result<bool>.Failure(CommunityPostLifecycleErrors.StaleWrite);
        }

        return Result<bool>.Success(true);
    }
}

/// <summary>
/// Validates the image reference of an authored post. A post may reference
/// only an uploaded Community image asset: the URL must be the authorized
/// upload read route, the referenced upload record must exist, and the acting
/// author must either own the upload or the asset must already be public
/// (attached to a published post — the same visibility the image read route
/// grants strangers). Arbitrary external URLs and another user's private
/// asset are rejected. Clearing the image (null) is always allowed, and an
/// unchanged stored value is not re-validated, so legacy posts that predate
/// upload enforcement remain load/edit/save when the author does not touch
/// the image.
/// </summary>
internal static class CommunityImageReferenceValidator
{
    public static async Task<Result<bool>> ValidateAsync(
        ICommunityDbContext dbContext,
        string? currentImageUrl,
        string? newImageUrl,
        Guid actingAuthorId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(newImageUrl))
        {
            return Result<bool>.Success(true);
        }

        string normalized = newImageUrl.Trim();
        if (string.Equals(normalized, currentImageUrl, StringComparison.Ordinal))
        {
            return Result<bool>.Success(true);
        }

        if (!CommunityImageUrls.TryParseImageId(normalized, out CommunityImageId imageId))
        {
            return Result<bool>.Failure(CommunityPostLifecycleErrors.InvalidImageReference);
        }

        CommunityImage? record = await dbContext.CommunityImages
            .AsNoTracking()
            .SingleOrDefaultAsync(image => image.Id == imageId, cancellationToken);

        if (record is null)
        {
            return Result<bool>.Failure(CommunityPostLifecycleErrors.InvalidImageReference);
        }

        bool isOwnUpload = record.UploadedBy == new UserId(actingAuthorId);
        bool isPubliclyAttached = await dbContext.Posts
            .AsNoTracking()
            .AnyAsync(
                p => p.Status == PostStatus.Published && p.ImageUrl == normalized,
                cancellationToken);

        return isOwnUpload || isPubliclyAttached
            ? Result<bool>.Success(true)
            : Result<bool>.Failure(CommunityPostLifecycleErrors.InvalidImageReference);
    }
}
