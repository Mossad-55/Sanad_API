using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Exceptions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Caregivers.Application.Abstractions.Data;
using Ahlena.Modules.Caregivers.Domain.Caregivers;

namespace Ahlena.Modules.Caregivers.Application.Onboarding;

/// <summary>
/// E07 append-only caregiver review note errors. The two codes are mapped
/// locally in <c>AdminCaregiversController</c> (the shared problem-details
/// mapper defaults an unknown code to 400 and is read-only), so a missing
/// caregiver is a real 404 and a bad text a 400. There is deliberately no 409
/// code: nothing in this phase is concurrent, mutable or stateful.
/// </summary>
public static class CaregiverReviewNoteErrors
{
    public static readonly Error CaregiverNotFound = new(
        "Caregivers.ReviewNote.CaregiverNotFound",
        "The caregiver was not found.");

    public static readonly Error InvalidText = new(
        "Caregivers.ReviewNote.InvalidText",
        "A review note of 1 to 2000 non-whitespace characters is required.");
}

/// <summary>
/// E07 admin review-note projection. An administrative note exposes its own id,
/// the caregiver it is about, the stored text, the authoring administrator and
/// the creation instant — never a caregiver identity document, phone number,
/// email address or medical fact.
/// </summary>
public sealed record AdminCaregiverReviewNoteItem(
    Guid NoteId,
    Guid CaregiverId,
    string Text,
    Guid AuthorUserId,
    DateTime CreatedOnUtc);

public sealed record AdminCaregiverReviewNotePage(
    IReadOnlyList<AdminCaregiverReviewNoteItem> Items,
    int Page,
    int PageSize,
    int TotalCount);

/// <summary>
/// One caregiver's notes. Paging is bounded and deterministic — newest first by
/// creation instant, then by note id descending as a stable tiebreak — and
/// <c>TotalCount</c> always reflects the caregiver's full note set, not the
/// page.
/// </summary>
public sealed record ListAdminCaregiverReviewNotesQuery(
    CaregiverId CaregiverId,
    int Page,
    int PageSize) : IQuery<AdminCaregiverReviewNotePage>;

public sealed class ListAdminCaregiverReviewNotesQueryHandler
    : IQueryHandler<ListAdminCaregiverReviewNotesQuery, AdminCaregiverReviewNotePage>
{
    internal const int DefaultPageSize = 20;
    internal const int MaxPageSize = 100;

    private readonly ICaregiversDbContext _dbContext;

    public ListAdminCaregiverReviewNotesQueryHandler(
        ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<AdminCaregiverReviewNotePage>> Handle(
        ListAdminCaregiverReviewNotesQuery request,
        CancellationToken cancellationToken)
    {
        // Paging follows the house read convention: it is clamped, never an
        // error, and pageSize above the cap is capped rather than rejected.
        int page = Math.Max(1, request.Page);
        int pageSize = Math.Clamp(
            request.PageSize <= 0 ? DefaultPageSize : request.PageSize, 1, MaxPageSize);

        bool caregiverExists = request.CaregiverId != CaregiverId.Empty &&
            await _dbContext.Caregivers
                .AsNoTracking()
                .AnyAsync(caregiver => caregiver.Id == request.CaregiverId, cancellationToken);

        if (!caregiverExists)
        {
            return Result<AdminCaregiverReviewNotePage>.Failure(
                CaregiverReviewNoteErrors.CaregiverNotFound);
        }

        IQueryable<CaregiverReviewNote> query =
            _dbContext.ReviewNotes
                .AsNoTracking()
                .Where(note => note.CaregiverId == request.CaregiverId);

        int totalCount = await query.CountAsync(cancellationToken);
        int skip = (int)Math.Min((long)(page - 1) * pageSize, int.MaxValue);

        List<CaregiverReviewNote> notes = await query
            .OrderByDescending(note => note.CreatedOnUtc)
            .ThenByDescending(note => note.Id)
            .Skip(skip)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return Result<AdminCaregiverReviewNotePage>.Success(
            new AdminCaregiverReviewNotePage(
                notes.Select(ToItem).ToArray(),
                page,
                pageSize,
                totalCount));
    }

    internal static AdminCaregiverReviewNoteItem ToItem(CaregiverReviewNote note) =>
        new(
            note.Id,
            note.CaregiverId.Value,
            note.Text,
            note.AuthorUserId.Value,
            note.CreatedOnUtc);
}

/// <summary>
/// Appends one note. The author is the authenticated actor and the instant is
/// the caller's UTC clock — both are supplied by the route, never by the
/// request body. Creating a note has no side effect: it never changes caregiver
/// status, certificates, ratings, payouts or review decisions, and it emits no
/// notification, email or domain event.
/// </summary>
public sealed record AddAdminCaregiverReviewNoteCommand(
    CaregiverId CaregiverId,
    UserId AuthorUserId,
    string? Text,
    DateTime UtcNow) : ICommand<AdminCaregiverReviewNoteItem>;

public sealed class AddAdminCaregiverReviewNoteCommandHandler
    : ICommandHandler<AddAdminCaregiverReviewNoteCommand, AdminCaregiverReviewNoteItem>
{
    private readonly ICaregiversDbContext _dbContext;

    public AddAdminCaregiverReviewNoteCommandHandler(
        ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<AdminCaregiverReviewNoteItem>> Handle(
        AddAdminCaregiverReviewNoteCommand request,
        CancellationToken cancellationToken)
    {
        // Pinned precedence: an unknown caregiver (404) is reported before the
        // text shape (400), exactly like the sibling caregiver review actions,
        // so a bad text never masks a missing caregiver.
        bool caregiverExists = request.CaregiverId != CaregiverId.Empty &&
            await _dbContext.Caregivers
                .AsNoTracking()
                .AnyAsync(caregiver => caregiver.Id == request.CaregiverId, cancellationToken);

        if (!caregiverExists)
        {
            return Result<AdminCaregiverReviewNoteItem>.Failure(
                CaregiverReviewNoteErrors.CaregiverNotFound);
        }

        if (string.IsNullOrWhiteSpace(request.Text) ||
            request.Text.Trim().Length > CaregiverReviewNote.MaximumTextLength)
        {
            return Result<AdminCaregiverReviewNoteItem>.Failure(
                CaregiverReviewNoteErrors.InvalidText);
        }

        CaregiverReviewNote note;

        try
        {
            note = CaregiverReviewNote.Write(
                request.CaregiverId,
                request.AuthorUserId,
                request.Text,
                request.UtcNow);
        }
        catch (DomainException)
        {
            return Result<AdminCaregiverReviewNoteItem>.Failure(
                CaregiverReviewNoteErrors.InvalidText);
        }

        _dbContext.ReviewNotes.Add(note);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<AdminCaregiverReviewNoteItem>.Success(
            ListAdminCaregiverReviewNotesQueryHandler.ToItem(note));
    }
}
