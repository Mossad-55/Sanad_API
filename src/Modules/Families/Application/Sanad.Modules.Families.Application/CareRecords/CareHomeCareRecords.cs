using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Domain.CareRecords;
using Sanad.Modules.Families.Domain.Elderlies;

namespace Sanad.Modules.Families.Application.CareRecords;

// ==================== CareHome list/detail (facility scope) ====================

/// <summary>
/// Care-home reads of one resident's CareHome-source records. The caller
/// passes the facility id resolved server-side by the API host from the
/// authenticated facility owner (never from the request body), plus the
/// resident id whose accepted-stay scope the host rechecked against the
/// CareHomes module. This handler revalidates the Families-scoped facts:
/// dependent existence and the record's care-home source/facility
/// attribution (locked D2/D3: no cross-source, no invented staff role).
/// </summary>
public sealed record ListCareHomeCareRecordsQuery(
    Guid FacilityId,
    ElderlyId ResidentId,
    DateOnly? VisitDateFrom = null,
    DateOnly? VisitDateTo = null,
    string? Search = null,
    int Page = CareRecordPageSettings.DefaultPage,
    int PageSize = CareRecordPageSettings.DefaultPageSize) : IQuery<CareRecordsPage<CareRecordResponse>>;

public sealed class ListCareHomeCareRecordsQueryHandler
    : IQueryHandler<ListCareHomeCareRecordsQuery, CareRecordsPage<CareRecordResponse>>
{
    private readonly IFamiliesDbContext _dbContext;

    public ListCareHomeCareRecordsQueryHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<CareRecordsPage<CareRecordResponse>>> Handle(
        ListCareHomeCareRecordsQuery request,
        CancellationToken cancellationToken)
    {
        Error? filterError = CareRecordFilterValidation.Validate(
            source: null, request.VisitDateFrom, request.VisitDateTo);
        var pageQuery = new CareRecordPageQuery(request.Search, request.Page, request.PageSize);
        filterError ??= pageQuery.Normalize();

        if (filterError is not null)
        {
            return filterError;
        }

        if (request.FacilityId == Guid.Empty)
        {
            return CareRecordErrors.AccessDenied;
        }

        if (!await _dbContext.Elderlies.AnyAsync(e => e.Id == request.ResidentId, cancellationToken))
        {
            return CareRecordErrors.DependentNotFound;
        }

        IQueryable<CareRecord> query = _dbContext.CareRecords
            .AsNoTracking()
            .Where(r => r.ElderlyId == request.ResidentId
                && r.Source == CareRecordSource.CareHome
                && r.AuthorFacilityId == request.FacilityId);

        query = query.Apply(
            source: null,
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

public sealed record GetCareHomeCareRecordQuery(
    Guid FacilityId,
    Guid RecordId) : IQuery<CareRecordResponse>;

public sealed class GetCareHomeCareRecordQueryHandler
    : IQueryHandler<GetCareHomeCareRecordQuery, CareRecordResponse>
{
    private readonly IFamiliesDbContext _dbContext;

    public GetCareHomeCareRecordQueryHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<CareRecordResponse>> Handle(
        GetCareHomeCareRecordQuery request,
        CancellationToken cancellationToken)
    {
        CareRecord? record = await _dbContext.CareRecords
            .AsNoTracking()
            .SingleOrDefaultAsync(
                r => r.Id == request.RecordId
                    && r.Source == CareRecordSource.CareHome
                    && r.AuthorFacilityId == request.FacilityId,
                cancellationToken);

        if (record is null)
        {
            return CareRecordErrors.NotFound;
        }

        return record.ToResponse();
    }
}

// ==================== CareHome create (own source) ====================

public sealed record CreateCareHomeCareRecordCommand(
    Guid FacilityId,
    UserId AuthorUserId,
    ElderlyId ResidentId,
    string HospitalName,
    DateOnly VisitDate,
    string Details,
    string CorrelationId) : ICommand<CareRecordResponse>;

public sealed class CreateCareHomeCareRecordCommandValidator
    : AbstractValidator<CreateCareHomeCareRecordCommand>
{
    public CreateCareHomeCareRecordCommandValidator()
    {
        RuleFor(c => c.FacilityId).NotEqual(Guid.Empty);
        RuleFor(c => c.AuthorUserId).NotEqual(UserId.Empty);
        RuleFor(c => c.ResidentId).NotEqual(ElderlyId.Empty);
        RuleFor(c => c.HospitalName).NotEmpty()
            .MaximumLength(CareRecord.MaximumHospitalNameLength);
        RuleFor(c => c.Details).NotEmpty()
            .MaximumLength(CareRecord.MaximumDetailsLength);
        RuleFor(c => c.CorrelationId).NotEmpty();
    }
}

public sealed class CreateCareHomeCareRecordCommandHandler
    : ICommandHandler<CreateCareHomeCareRecordCommand, CareRecordResponse>
{
    private readonly IFamiliesDbContext _dbContext;
    private readonly IDateTimeProvider _dateTimeProvider;

    public CreateCareHomeCareRecordCommandHandler(
        IFamiliesDbContext dbContext,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<CareRecordResponse>> Handle(
        CreateCareHomeCareRecordCommand request,
        CancellationToken cancellationToken)
    {
        if (!await _dbContext.Elderlies.AnyAsync(
                e => e.Id == request.ResidentId, cancellationToken))
        {
            return CareRecordErrors.DependentNotFound;
        }

        try
        {
            DateTime utcNow = _dateTimeProvider.UtcNow;

            CareRecord record = CareRecord.CreateForCareHome(
                request.ResidentId,
                request.AuthorUserId,
                request.FacilityId,
                request.HospitalName,
                request.VisitDate,
                request.Details,
                utcNow);

            _dbContext.CareRecords.Add(record);
            _dbContext.CareRecordAudits.Add(CareRecordAudit.RecordAuthored(
                record.Id,
                record.ElderlyId,
                record.Source,
                request.AuthorUserId,
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

// ==================== CareHome update (own source) ====================

public sealed record UpdateCareHomeCareRecordCommand(
    Guid FacilityId,
    UserId AuthorUserId,
    Guid RecordId,
    string HospitalName,
    DateOnly VisitDate,
    string Details,
    int ExpectedVersion,
    string CorrelationId) : ICommand<CareRecordResponse>;

public sealed class UpdateCareHomeCareRecordCommandValidator
    : AbstractValidator<UpdateCareHomeCareRecordCommand>
{
    public UpdateCareHomeCareRecordCommandValidator()
    {
        RuleFor(c => c.FacilityId).NotEqual(Guid.Empty);
        RuleFor(c => c.AuthorUserId).NotEqual(UserId.Empty);
        RuleFor(c => c.RecordId).NotEqual(Guid.Empty);
        RuleFor(c => c.HospitalName).NotEmpty()
            .MaximumLength(CareRecord.MaximumHospitalNameLength);
        RuleFor(c => c.Details).NotEmpty()
            .MaximumLength(CareRecord.MaximumDetailsLength);
        RuleFor(c => c.ExpectedVersion).GreaterThan(0);
        RuleFor(c => c.CorrelationId).NotEmpty();
    }
}

public sealed class UpdateCareHomeCareRecordCommandHandler
    : ICommandHandler<UpdateCareHomeCareRecordCommand, CareRecordResponse>
{
    private readonly IFamiliesDbContext _dbContext;
    private readonly IDateTimeProvider _dateTimeProvider;

    public UpdateCareHomeCareRecordCommandHandler(
        IFamiliesDbContext dbContext,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<CareRecordResponse>> Handle(
        UpdateCareHomeCareRecordCommand request,
        CancellationToken cancellationToken)
    {
        CareRecord? record = await _dbContext.CareRecords.SingleOrDefaultAsync(
            r => r.Id == request.RecordId,
            cancellationToken);

        if (record is null)
        {
            return CareRecordErrors.NotFound;
        }

        if (record.Source != CareRecordSource.CareHome
            || record.AuthorFacilityId != request.FacilityId)
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
                request.AuthorUserId,
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

// ==================== CareHome delete (own source) ====================

public sealed record DeleteCareHomeCareRecordCommand(
    Guid FacilityId,
    UserId AuthorUserId,
    Guid RecordId,
    int ExpectedVersion,
    string CorrelationId) : ICommand;

public sealed class DeleteCareHomeCareRecordCommandValidator
    : AbstractValidator<DeleteCareHomeCareRecordCommand>
{
    public DeleteCareHomeCareRecordCommandValidator()
    {
        RuleFor(c => c.FacilityId).NotEqual(Guid.Empty);
        RuleFor(c => c.AuthorUserId).NotEqual(UserId.Empty);
        RuleFor(c => c.RecordId).NotEqual(Guid.Empty);
        RuleFor(c => c.ExpectedVersion).GreaterThan(0);
        RuleFor(c => c.CorrelationId).NotEmpty();
    }
}

public sealed class DeleteCareHomeCareRecordCommandHandler
    : ICommandHandler<DeleteCareHomeCareRecordCommand>
{
    private readonly IFamiliesDbContext _dbContext;
    private readonly IDateTimeProvider _dateTimeProvider;

    public DeleteCareHomeCareRecordCommandHandler(
        IFamiliesDbContext dbContext,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result> Handle(
        DeleteCareHomeCareRecordCommand request,
        CancellationToken cancellationToken)
    {
        CareRecord? record = await _dbContext.CareRecords.SingleOrDefaultAsync(
            r => r.Id == request.RecordId,
            cancellationToken);

        if (record is null)
        {
            return Result.Failure(CareRecordErrors.NotFound);
        }

        if (record.Source != CareRecordSource.CareHome
            || record.AuthorFacilityId != request.FacilityId)
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

            _dbContext.CareRecordAudits.Add(CareRecordAudit.RecordAuthored(
                record.Id,
                record.ElderlyId,
                record.Source,
                request.AuthorUserId,
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
