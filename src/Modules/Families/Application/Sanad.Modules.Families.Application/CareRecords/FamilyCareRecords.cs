using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Application.Families;
using Sanad.Modules.Families.Domain.CareRecords;
using Sanad.Modules.Families.Domain.Families;

namespace Sanad.Modules.Families.Application.CareRecords;

// ====================== Family read: paged timeline ======================

/// <summary>
/// Family read of a dependent's combined timeline across ALL THREE sources
/// (Owner-locked). Every family member role may read; only Owner/Editor may
/// write, and only family-source records.
/// </summary>
public sealed record ListFamilyCareRecordsQuery(
    UserId UserId,
    ElderlyId DependentId,
    CareRecordSource? Source = null,
    DateOnly? VisitDateFrom = null,
    DateOnly? VisitDateTo = null,
    string? Search = null,
    int Page = CareRecordPageSettings.DefaultPage,
    int PageSize = CareRecordPageSettings.DefaultPageSize) : IQuery<CareRecordsPage<CareRecordResponse>>;

public sealed class ListFamilyCareRecordsQueryHandler : IQueryHandler<
    ListFamilyCareRecordsQuery, CareRecordsPage<CareRecordResponse>>
{
    private readonly IFamiliesDbContext _dbContext;

    public ListFamilyCareRecordsQueryHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<CareRecordsPage<CareRecordResponse>>> Handle(
        ListFamilyCareRecordsQuery request,
        CancellationToken cancellationToken)
    {
        Family? family = await FamilyAccess.ResolveFamilyAsync(_dbContext, request.UserId, cancellationToken);

        if (family is null || !FamilyAccess.IsMember(family, request.UserId))
        {
            return CareRecordErrors.AccessDenied;
        }

        if (!await _dbContext.Elderlies.AnyAsync(
                e => e.Id == request.DependentId && e.FamilyId == family.Id,
                cancellationToken))
        {
            return CareRecordErrors.DependentNotFound;
        }

        Error? filterError = CareRecordFilterValidation.Validate(
            request.Source, request.VisitDateFrom, request.VisitDateTo);
        var pageQuery = new CareRecordPageQuery(request.Search, request.Page, request.PageSize);
        filterError ??= pageQuery.Normalize();

        if (filterError is not null)
        {
            return filterError;
        }

        IQueryable<CareRecord> query = _dbContext.CareRecords
            .AsNoTracking()
            .Where(r => r.ElderlyId == request.DependentId);

        query = query.Apply(
            request.Source,
            request.VisitDateFrom,
            request.VisitDateTo,
            pageQuery.NormalizedSearch);

        int totalCount = await query.CountAsync(cancellationToken);

        List<CareRecordResponse> items = (await query
                .OrderNewestFirst()
                .Skip((pageQuery.Page - 1) * pageQuery.PageSize)
                .Take(pageQuery.PageSize)
                .ToListAsync(cancellationToken))
            .Select(record => record.ToResponse())
            .ToList();

        return CareRecordPageQuery.CreatePage(items, pageQuery.Page, pageQuery.PageSize, totalCount);
    }
}

// ===================== Family read: single record =====================

public sealed record GetFamilyCareRecordQuery(
    UserId UserId,
    ElderlyId DependentId,
    Guid RecordId) : IQuery<CareRecordResponse>;

public sealed class GetFamilyCareRecordQueryHandler
    : IQueryHandler<GetFamilyCareRecordQuery, CareRecordResponse>
{
    private readonly IFamiliesDbContext _dbContext;

    public GetFamilyCareRecordQueryHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<CareRecordResponse>> Handle(
        GetFamilyCareRecordQuery request,
        CancellationToken cancellationToken)
    {
        Family? family = await FamilyAccess.ResolveFamilyAsync(_dbContext, request.UserId, cancellationToken);

        if (family is null || !FamilyAccess.IsMember(family, request.UserId))
        {
            return CareRecordErrors.AccessDenied;
        }

        CareRecord? record = await _dbContext.CareRecords
            .AsNoTracking()
            .SingleOrDefaultAsync(
                r => r.Id == request.RecordId
                    && r.ElderlyId == request.DependentId
                    && _dbContext.Elderlies.Any(e => e.Id == r.ElderlyId && e.FamilyId == family.Id),
                cancellationToken);

        return record is null
            ? CareRecordErrors.NotFound
            : record.ToResponse();
    }
}

// ======================= Family create (own source) =======================

public sealed record CreateFamilyCareRecordCommand(
    UserId UserId,
    ElderlyId DependentId,
    string HospitalName,
    DateOnly VisitDate,
    string Details,
    string CorrelationId) : ICommand<CareRecordResponse>;

public sealed class CreateFamilyCareRecordCommandValidator : AbstractValidator<CreateFamilyCareRecordCommand>
{
    public CreateFamilyCareRecordCommandValidator()
    {
        RuleFor(c => c.UserId).NotEqual(UserId.Empty);
        RuleFor(c => c.DependentId).NotEqual(ElderlyId.Empty);
        RuleFor(c => c.HospitalName).NotEmpty()
            .MaximumLength(CareRecord.MaximumHospitalNameLength);
        RuleFor(c => c.Details).NotEmpty()
            .MaximumLength(CareRecord.MaximumDetailsLength);
        RuleFor(c => c.CorrelationId).NotEmpty();
    }
}

public sealed class CreateFamilyCareRecordCommandHandler
    : ICommandHandler<CreateFamilyCareRecordCommand, CareRecordResponse>
{
    private readonly IFamiliesDbContext _dbContext;
    private readonly IDateTimeProvider _dateTimeProvider;

    public CreateFamilyCareRecordCommandHandler(
        IFamiliesDbContext dbContext,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<CareRecordResponse>> Handle(
        CreateFamilyCareRecordCommand request,
        CancellationToken cancellationToken)
    {
        Family? family = await FamilyAccess.ResolveFamilyAsync(_dbContext, request.UserId, cancellationToken);

        if (family is null || !FamilyAccess.CanManage(family, request.UserId))
        {
            return CareRecordErrors.AccessDenied;
        }

        if (!await _dbContext.Elderlies.AnyAsync(
                e => e.Id == request.DependentId && e.FamilyId == family.Id,
                cancellationToken))
        {
            return CareRecordErrors.DependentNotFound;
        }

        try
        {
            DateTime utcNow = _dateTimeProvider.UtcNow;

            CareRecord record = CareRecord.CreateForFamily(
                request.DependentId,
                request.UserId,
                family.Id,
                request.HospitalName,
                request.VisitDate,
                request.Details,
                utcNow);

            _dbContext.CareRecords.Add(record);
            _dbContext.CareRecordAudits.Add(CareRecordAudit.RecordAuthored(
                record.Id,
                record.ElderlyId,
                record.Source,
                request.UserId,
                CareRecordAuditAction.Created,
                request.CorrelationId,
                utcNow));

            await _dbContext.SaveChangesAsync(cancellationToken);

            return record.ToResponse();
        }
        catch (DomainException)
        {
            return CareRecordErrors.Invalid;
        }
    }
}

// ======================= Family update (own source) =======================

public sealed record UpdateFamilyCareRecordCommand(
    UserId UserId,
    ElderlyId DependentId,
    Guid RecordId,
    string HospitalName,
    DateOnly VisitDate,
    string Details,
    int ExpectedVersion,
    string CorrelationId) : ICommand<CareRecordResponse>;

public sealed class UpdateFamilyCareRecordCommandValidator : AbstractValidator<UpdateFamilyCareRecordCommand>
{
    public UpdateFamilyCareRecordCommandValidator()
    {
        RuleFor(c => c.UserId).NotEqual(UserId.Empty);
        RuleFor(c => c.DependentId).NotEqual(ElderlyId.Empty);
        RuleFor(c => c.RecordId).NotEqual(Guid.Empty);
        RuleFor(c => c.HospitalName).NotEmpty()
            .MaximumLength(CareRecord.MaximumHospitalNameLength);
        RuleFor(c => c.Details).NotEmpty()
            .MaximumLength(CareRecord.MaximumDetailsLength);
        RuleFor(c => c.ExpectedVersion).GreaterThan(0);
        RuleFor(c => c.CorrelationId).NotEmpty();
    }
}

public sealed class UpdateFamilyCareRecordCommandHandler
    : ICommandHandler<UpdateFamilyCareRecordCommand, CareRecordResponse>
{
    private readonly IFamiliesDbContext _dbContext;
    private readonly IDateTimeProvider _dateTimeProvider;

    public UpdateFamilyCareRecordCommandHandler(
        IFamiliesDbContext dbContext,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<CareRecordResponse>> Handle(
        UpdateFamilyCareRecordCommand request,
        CancellationToken cancellationToken)
    {
        Family? family = await FamilyAccess.ResolveFamilyAsync(_dbContext, request.UserId, cancellationToken);

        if (family is null || !FamilyAccess.CanManage(family, request.UserId))
        {
            return CareRecordErrors.AccessDenied;
        }

        CareRecord? record = await _dbContext.CareRecords.SingleOrDefaultAsync(
            r => r.Id == request.RecordId
                && r.ElderlyId == request.DependentId
                && _dbContext.Elderlies.Any(e => e.Id == r.ElderlyId && e.FamilyId == family.Id),
            cancellationToken);

        // Family editors may update family-source records only. A
        // Caregiver/CareHome-source record under an authorized dependent is a
        // role/source denial (403), never a 404.
        if (record is null)
        {
            bool existsForDependent = await _dbContext.CareRecords.AnyAsync(
                r => r.Id == request.RecordId && r.ElderlyId == request.DependentId,
                cancellationToken);

            return existsForDependent
                ? CareRecordErrors.AccessDenied
                : CareRecordErrors.NotFound;
        }

        if (record.Source != CareRecordSource.Family)
        {
            return CareRecordErrors.AccessDenied;
        }

        if (record.Version != request.ExpectedVersion)
        {
            return CareRecordErrors.ConcurrencyConflict;
        }

        try
        {
            DateTime utcNow = _dateTimeProvider.UtcNow;

            record.Update(request.HospitalName, request.VisitDate, request.Details, utcNow);

            _dbContext.CareRecordAudits.Add(CareRecordAudit.RecordAuthored(
                record.Id,
                record.ElderlyId,
                record.Source,
                request.UserId,
                CareRecordAuditAction.Updated,
                request.CorrelationId,
                utcNow));

            await _dbContext.SaveChangesAsync(cancellationToken);

            return record.ToResponse();
        }
        catch (DbUpdateConcurrencyException)
        {
            return CareRecordErrors.ConcurrencyConflict;
        }
        catch (DomainException)
        {
            return CareRecordErrors.Invalid;
        }
    }
}

// ======================= Family delete (own source) =======================

public sealed record DeleteFamilyCareRecordCommand(
    UserId UserId,
    ElderlyId DependentId,
    Guid RecordId,
    int ExpectedVersion,
    string CorrelationId) : ICommand;

public sealed class DeleteFamilyCareRecordCommandValidator : AbstractValidator<DeleteFamilyCareRecordCommand>
{
    public DeleteFamilyCareRecordCommandValidator()
    {
        RuleFor(c => c.UserId).NotEqual(UserId.Empty);
        RuleFor(c => c.DependentId).NotEqual(ElderlyId.Empty);
        RuleFor(c => c.RecordId).NotEqual(Guid.Empty);
        RuleFor(c => c.ExpectedVersion).GreaterThan(0);
        RuleFor(c => c.CorrelationId).NotEmpty();
    }
}

public sealed class DeleteFamilyCareRecordCommandHandler : ICommandHandler<DeleteFamilyCareRecordCommand>
{
    private readonly IFamiliesDbContext _dbContext;
    private readonly IDateTimeProvider _dateTimeProvider;

    public DeleteFamilyCareRecordCommandHandler(
        IFamiliesDbContext dbContext,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result> Handle(
        DeleteFamilyCareRecordCommand request,
        CancellationToken cancellationToken)
    {
        Family? family = await FamilyAccess.ResolveFamilyAsync(_dbContext, request.UserId, cancellationToken);

        if (family is null || !FamilyAccess.CanManage(family, request.UserId))
        {
            return Result.Failure(CareRecordErrors.AccessDenied);
        }

        CareRecord? record = await _dbContext.CareRecords.SingleOrDefaultAsync(
            r => r.Id == request.RecordId
                && r.ElderlyId == request.DependentId
                && _dbContext.Elderlies.Any(e => e.Id == r.ElderlyId && e.FamilyId == family.Id),
            cancellationToken);

        if (record is null)
        {
            bool existsForDependent = await _dbContext.CareRecords.AnyAsync(
                r => r.Id == request.RecordId && r.ElderlyId == request.DependentId,
                cancellationToken);

            return Result.Failure(
                existsForDependent ? CareRecordErrors.AccessDenied : CareRecordErrors.NotFound);
        }

        if (record.Source != CareRecordSource.Family)
        {
            return Result.Failure(CareRecordErrors.AccessDenied);
        }

        if (record.Version != request.ExpectedVersion)
        {
            return Result.Failure(CareRecordErrors.ConcurrencyConflict);
        }

        try
        {
            DateTime utcNow = _dateTimeProvider.UtcNow;

            // Deletion attribution: the append-only audit row outlives the
            // record row and states who deleted which source's record.
            _dbContext.CareRecordAudits.Add(CareRecordAudit.RecordAuthored(
                record.Id,
                record.ElderlyId,
                record.Source,
                request.UserId,
                CareRecordAuditAction.Deleted,
                request.CorrelationId,
                utcNow));

            _dbContext.CareRecords.Remove(record);

            await _dbContext.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(CareRecordErrors.ConcurrencyConflict);
        }
    }
}
