using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Application.Families;
using Sanad.Modules.Families.Domain.Activities;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Domain.Notes;

namespace Sanad.Modules.Families.Application.Notes;

// ================================= Responses =================================

public sealed record ElderlyNoteResponse(
    Guid Id,
    Guid DependentId,
    Guid AuthorUserId,
    string Title,
    string Description,
    NoteCategory Category,
    string CategoryNameAr,
    string CategoryNameEn,
    NotePriority Priority,
    DateTime CreatedOnUtc,
    DateTime UpdatedOnUtc);

public sealed record NoteLookupOptionResponse(
    int Id,
    string NameAr,
    string NameEn);

public sealed record NoteLookupsResponse(
    IReadOnlyList<NoteLookupOptionResponse> Categories,
    IReadOnlyList<NoteLookupOptionResponse> Priorities);

internal static class ElderlyNoteMappings
{
    public static ElderlyNoteResponse ToResponse(this ElderlyNote note) =>
        new(
            note.Id.Value,
            note.ElderlyId.Value,
            note.AuthorUserId.Value,
            note.Title,
            note.Description,
            note.Category,
            GetCategoryNameAr(note.Category),
            GetCategoryNameEn(note.Category),
            note.Priority,
            note.CreatedOnUtc,
            note.UpdatedOnUtc);

    public static string GetCategoryNameAr(NoteCategory category) => category switch
    {
        NoteCategory.Nutrition => "تغذية وشهية",
        NoteCategory.Sleep => "نوم وراحة",
        NoteCategory.HealthSymptoms => "صحة وأعراض",
        NoteCategory.PhysicalTherapy => "علاج طبيعي وحركة",
        NoteCategory.MoodBehavior => "مزاج وسلوك",
        NoteCategory.DailyRoutine => "روتين يومي ونظافة",
        NoteCategory.General => "ملاحظة عامة",
        _ => "عام"
    };

    public static string GetCategoryNameEn(NoteCategory category) => category switch
    {
        NoteCategory.Nutrition => "Nutrition & Appetite",
        NoteCategory.Sleep => "Sleep & Rest",
        NoteCategory.HealthSymptoms => "Health & Symptoms",
        NoteCategory.PhysicalTherapy => "Physical Therapy & Mobility",
        NoteCategory.MoodBehavior => "Mood & Behavior",
        NoteCategory.DailyRoutine => "Daily Routine & Hygiene",
        NoteCategory.General => "General Observation",
        _ => "General"
    };
}

// ================================ Add Note ================================

public sealed record AddElderlyNoteCommand(
    UserId UserId,
    ElderlyId DependentId,
    string Title,
    string Description,
    NoteCategory Category,
    NotePriority Priority) : ICommand<ElderlyNoteResponse>;

public sealed class AddElderlyNoteCommandValidator : AbstractValidator<AddElderlyNoteCommand>
{
    public AddElderlyNoteCommandValidator()
    {
        RuleFor(c => c.UserId).NotEqual(UserId.Empty);
        RuleFor(c => c.DependentId).NotEqual(ElderlyId.Empty);
        RuleFor(c => c.Title).NotEmpty().MaximumLength(ElderlyNote.MaximumTitleLength);
        RuleFor(c => c.Description).NotEmpty().MaximumLength(ElderlyNote.MaximumDescriptionLength);
        RuleFor(c => c.Category).IsInEnum();
        RuleFor(c => c.Priority).IsInEnum();
    }
}

public sealed class AddElderlyNoteCommandHandler : ICommandHandler<AddElderlyNoteCommand, ElderlyNoteResponse>
{
    private readonly IFamiliesDbContext _dbContext;

    public AddElderlyNoteCommandHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<ElderlyNoteResponse>> Handle(AddElderlyNoteCommand request, CancellationToken cancellationToken)
    {
        Family? family = await FamilyAccess.ResolveFamilyAsync(_dbContext, request.UserId, cancellationToken);
        if (family is null || !FamilyAccess.CanManage(family, request.UserId))
        {
            return NoteErrors.AccessDenied;
        }

        bool dependentExists = await _dbContext.Elderlies.AnyAsync(
            e => e.Id == request.DependentId && e.FamilyId == family.Id,
            cancellationToken);

        if (!dependentExists)
        {
            return NoteErrors.DependentNotFound;
        }

        try
        {
            var note = ElderlyNote.Create(
                request.DependentId,
                request.UserId,
                request.Title,
                request.Description,
                request.Category,
                request.Priority);

            _dbContext.ElderlyNotes.Add(note);

            // Audit activity log
            var activityLog = ElderlyActivityLog.Create(
                request.DependentId,
                request.UserId,
                ElderlyActivityType.AddNote,
                $"إضافة ملاحظة: {note.Title}");

            _dbContext.ElderlyActivityLogs.Add(activityLog);

            await _dbContext.SaveChangesAsync(cancellationToken);

            return note.ToResponse();
        }
        catch (DomainException)
        {
            return NoteErrors.InvalidNote;
        }
    }
}

// =============================== Update Note ===============================

public sealed record UpdateElderlyNoteCommand(
    UserId UserId,
    ElderlyId DependentId,
    ElderlyNoteId NoteId,
    string Title,
    string Description,
    NoteCategory Category,
    NotePriority Priority) : ICommand<ElderlyNoteResponse>;

public sealed class UpdateElderlyNoteCommandValidator : AbstractValidator<UpdateElderlyNoteCommand>
{
    public UpdateElderlyNoteCommandValidator()
    {
        RuleFor(c => c.UserId).NotEqual(UserId.Empty);
        RuleFor(c => c.DependentId).NotEqual(ElderlyId.Empty);
        RuleFor(c => c.NoteId).NotEqual(ElderlyNoteId.Empty);
        RuleFor(c => c.Title).NotEmpty().MaximumLength(ElderlyNote.MaximumTitleLength);
        RuleFor(c => c.Description).NotEmpty().MaximumLength(ElderlyNote.MaximumDescriptionLength);
        RuleFor(c => c.Category).IsInEnum();
        RuleFor(c => c.Priority).IsInEnum();
    }
}

public sealed class UpdateElderlyNoteCommandHandler : ICommandHandler<UpdateElderlyNoteCommand, ElderlyNoteResponse>
{
    private readonly IFamiliesDbContext _dbContext;

    public UpdateElderlyNoteCommandHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<ElderlyNoteResponse>> Handle(UpdateElderlyNoteCommand request, CancellationToken cancellationToken)
    {
        Family? family = await FamilyAccess.ResolveFamilyAsync(_dbContext, request.UserId, cancellationToken);
        if (family is null || !FamilyAccess.CanManage(family, request.UserId))
        {
            return NoteErrors.AccessDenied;
        }

        ElderlyNote? note = await _dbContext.ElderlyNotes.SingleOrDefaultAsync(
            n => n.Id == request.NoteId && n.ElderlyId == request.DependentId,
            cancellationToken);

        if (note is null)
        {
            return NoteErrors.NotFound;
        }

        try
        {
            note.Update(request.Title, request.Description, request.Category, request.Priority);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return note.ToResponse();
        }
        catch (DomainException)
        {
            return NoteErrors.InvalidNote;
        }
    }
}

// =============================== Delete Note ===============================

public sealed record DeleteElderlyNoteCommand(
    UserId UserId,
    ElderlyId DependentId,
    ElderlyNoteId NoteId) : ICommand;

public sealed class DeleteElderlyNoteCommandHandler : ICommandHandler<DeleteElderlyNoteCommand>
{
    private readonly IFamiliesDbContext _dbContext;

    public DeleteElderlyNoteCommandHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result> Handle(DeleteElderlyNoteCommand request, CancellationToken cancellationToken)
    {
        Family? family = await FamilyAccess.ResolveFamilyAsync(_dbContext, request.UserId, cancellationToken);
        if (family is null || !FamilyAccess.CanManage(family, request.UserId))
        {
            return Result.Failure(NoteErrors.AccessDenied);
        }

        ElderlyNote? note = await _dbContext.ElderlyNotes.SingleOrDefaultAsync(
            n => n.Id == request.NoteId && n.ElderlyId == request.DependentId,
            cancellationToken);

        if (note is null)
        {
            return Result.Failure(NoteErrors.NotFound);
        }

        _dbContext.ElderlyNotes.Remove(note);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

// =============================== List Notes ===============================

public sealed record ListElderlyNotesQuery(
    UserId UserId,
    ElderlyId DependentId,
    NoteCategory? Category = null,
    NotePriority? Priority = null) : IQuery<IReadOnlyList<ElderlyNoteResponse>>;

public sealed class ListElderlyNotesQueryHandler : IQueryHandler<ListElderlyNotesQuery, IReadOnlyList<ElderlyNoteResponse>>
{
    private readonly IFamiliesDbContext _dbContext;

    public ListElderlyNotesQueryHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<IReadOnlyList<ElderlyNoteResponse>>> Handle(ListElderlyNotesQuery request, CancellationToken cancellationToken)
    {
        Family? family = await FamilyAccess.ResolveFamilyAsync(_dbContext, request.UserId, cancellationToken);
        if (family is null)
        {
            return NoteErrors.AccessDenied;
        }

        var query = _dbContext.ElderlyNotes
            .AsNoTracking()
            .Where(n => n.ElderlyId == request.DependentId);

        if (request.Category.HasValue)
        {
            query = query.Where(n => n.Category == request.Category.Value);
        }

        if (request.Priority.HasValue)
        {
            query = query.Where(n => n.Priority == request.Priority.Value);
        }

        List<ElderlyNote> notes = await query
            .OrderByDescending(n => n.CreatedOnUtc)
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<ElderlyNoteResponse>>.Success(
            notes.Select(n => n.ToResponse()).ToList());
    }
}

// ============================== Lookups Query ==============================

public sealed record GetNoteLookupsQuery : IQuery<NoteLookupsResponse>;

public sealed class GetNoteLookupsQueryHandler : IQueryHandler<GetNoteLookupsQuery, NoteLookupsResponse>
{
    public Task<Result<NoteLookupsResponse>> Handle(GetNoteLookupsQuery request, CancellationToken cancellationToken)
    {
        var categories = Enum.GetValues<NoteCategory>()
            .Select(c => new NoteLookupOptionResponse(
                (int)c,
                ElderlyNoteMappings.GetCategoryNameAr(c),
                ElderlyNoteMappings.GetCategoryNameEn(c)))
            .ToList();

        var priorities = Enum.GetValues<NotePriority>()
            .Select(p => new NoteLookupOptionResponse(
                (int)p,
                p switch { NotePriority.Low => "منخفضة", NotePriority.Medium => "متوسطة", NotePriority.High => "عالية", _ => "" },
                p.ToString()))
            .ToList();

        return Task.FromResult(Result<NoteLookupsResponse>.Success(
            new NoteLookupsResponse(categories, priorities)));
    }
}