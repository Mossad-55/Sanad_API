using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.Abstractions;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Idempotency;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Exceptions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.BuildingBlocks.Infrastructure.Persistence;
using Ahlena.Modules.Families.Application.Abstractions.Data;
using Ahlena.Modules.Families.Application.Families;
using Ahlena.Modules.Families.Domain.CareRecords;
using Ahlena.Modules.Families.Domain.Families;
using IdempotencyRecord = Ahlena.Modules.Families.Domain.Idempotency.IdempotencyRequest;

namespace Ahlena.Modules.Families.Application.CareRecords;

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
    string CorrelationId,
    IdempotencyRequest? Idempotency = null)
    : ICommand<IdempotentOutcome<CareRecordResponse>>;

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
    : ICommandHandler<CreateFamilyCareRecordCommand, IdempotentOutcome<CareRecordResponse>>
{
    private static readonly Error IdempotencyConflict = new(
        "Families.CareRecords.IdempotencyConflict",
        "This Idempotency-Key was already used with a different request.");

    private readonly IFamiliesDbContext _dbContext;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IIdempotentResponseSnapshotter _snapshotter;

    public CreateFamilyCareRecordCommandHandler(
        IFamiliesDbContext dbContext,
        IDateTimeProvider dateTimeProvider,
        IIdempotentResponseSnapshotter snapshotter)
    {
        _dbContext = dbContext;
        _dateTimeProvider = dateTimeProvider;
        _snapshotter = snapshotter;
    }

    public async Task<Result<IdempotentOutcome<CareRecordResponse>>> Handle(
        CreateFamilyCareRecordCommand request,
        CancellationToken cancellationToken)
    {
        Family? family = await FamilyAccess.ResolveFamilyAsync(_dbContext, request.UserId, cancellationToken);

        if (family is null || !FamilyAccess.CanManage(family, request.UserId))
        {
            return Result<IdempotentOutcome<CareRecordResponse>>.Failure(
                CareRecordErrors.AccessDenied);
        }

        if (!await _dbContext.Elderlies.AnyAsync(
                e => e.Id == request.DependentId && e.FamilyId == family.Id,
                cancellationToken))
        {
            return Result<IdempotentOutcome<CareRecordResponse>>.Failure(
                CareRecordErrors.DependentNotFound);
        }

        if (request.Idempotency is not null)
        {
            IdempotencyRecord? stored = await FindStoredAsync(
                request.UserId,
                request.Idempotency.Key.Value,
                cancellationToken);

            if (stored is not null)
            {
                return Replay(stored, request.Idempotency);
            }
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

            var response = record.ToResponse();

            _dbContext.CareRecords.Add(record);
            _dbContext.CareRecordAudits.Add(CareRecordAudit.RecordAuthored(
                record.Id,
                record.ElderlyId,
                record.Source,
                request.UserId,
                CareRecordAuditAction.Created,
                request.CorrelationId,
                utcNow));

            if (request.Idempotency is not null)
            {
                _dbContext.IdempotencyRequests.Add(
                    IdempotencyRecord.Create(
                        request.UserId,
                        IdempotencyOperations.FamilyCareRecordCreate,
                        request.Idempotency.Key.Value,
                        request.Idempotency.Fingerprint,
                        record.Id,
                        201,
                        _snapshotter.Snapshot(response),
                        utcNow));
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            return Result<IdempotentOutcome<CareRecordResponse>>.Success(
                IdempotentOutcome<CareRecordResponse>.Fresh(response, 201));
        }
        catch (DbUpdateException exception) when (
            request.Idempotency is not null &&
            PostgresErrors.IsUniqueViolation(
                exception,
                IdempotencyRecord.UniqueIndexName))
        {
            _dbContext.DetachAdded<CareRecord>();
            _dbContext.DetachAdded<CareRecordAudit>();
            _dbContext.DetachAdded<IdempotencyRecord>();

            IdempotencyRecord? winner = await FindStoredAsync(
                request.UserId,
                request.Idempotency.Key.Value,
                cancellationToken);

            if (winner is null)
            {
                throw;
            }

            return Replay(winner, request.Idempotency);
        }
        catch (DomainException)
        {
            return Result<IdempotentOutcome<CareRecordResponse>>.Failure(
                CareRecordErrors.Invalid);
        }
    }

    private async Task<IdempotencyRecord?> FindStoredAsync(
        UserId actor,
        string key,
        CancellationToken cancellationToken) =>
        await _dbContext.IdempotencyRequests
            .AsNoTracking()
            .SingleOrDefaultAsync(
                row => row.ActorId == actor &&
                    row.Operation == IdempotencyOperations.FamilyCareRecordCreate &&
                    row.Key == key,
                cancellationToken);

    private static Result<IdempotentOutcome<CareRecordResponse>> Replay(
        IdempotencyRecord stored,
        IdempotencyRequest idempotency) =>
        stored.Fingerprint == idempotency.Fingerprint
            ? Result<IdempotentOutcome<CareRecordResponse>>.Success(
                IdempotentOutcome<CareRecordResponse>.Replay(
                    stored.ResponseStatus,
                    stored.ResponseBody))
            : Result<IdempotentOutcome<CareRecordResponse>>.Failure(
                IdempotencyConflict);
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
