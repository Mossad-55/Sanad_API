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
using Ahlena.Modules.Families.Domain.Bookings;
using Ahlena.Modules.Families.Domain.CareRecords;
using Ahlena.Modules.Families.Domain.Elderlies;
using IdempotencyRecord = Ahlena.Modules.Families.Domain.Idempotency.IdempotencyRequest;

namespace Ahlena.Modules.Families.Application.CareRecords;

// ==================== Caregiver recipient picker ====================

/// <summary>
/// The locked authorized-recipient picker: paged, bilingual (Arabic +
/// English) names only, never medical data. Scope is the caregiver's own
/// booking window — Confirmed/InProgress plus the 30-day completion grace —
/// evaluated fresh on every picker call, so a permission revoked between this
/// GET and a later create POST is caught by the create's revalidation.
/// </summary>
public sealed record ListCaregiverRecipientsQuery(
    CaregiverId CaregiverId,
    string? Search = null,
    int Page = CareRecordPageSettings.DefaultPage,
    int PageSize = CareRecordPageSettings.DefaultPageSize) : IQuery<CareRecordsPage<CareRecordRecipientResponse>>;

/// <summary>
/// The exact query shape of the caregiver recipient picker, kept as a named
/// builder so the provider-level translation facts can pin the SQL that ships
/// (ILIKE + ESCAPE, the deterministic mapped-property ordering, the paged
/// SKIP/TAKE and the booking-window EXISTS) without opening a connection.
/// </summary>
/// <remarks>
/// SQL translation (PostgreSQL, same defect family as the resident read
/// pinned by <c>CareHomeResidentsPostgresTests</c>): the names are value
/// objects persisted through EF value converters, so the search must use the
/// mapped properties with the 3-argument <c>EF.Functions.ILike</c> overload
/// and the ordering must be by the mapped properties. The previous
/// <c>.Value.ToLower().Contains(...)</c> search and
/// <c>.OrderBy(e =&gt; e.ArabicFullName.Value)</c> shapes unwrap members of
/// the converted type and are rejected by the Npgsql translator, which is
/// what made every picker search 500.
/// </remarks>
public static class CaregiverRecipientPickerQuery
{
    /// <summary>The ILIKE escape character; the pattern is the escaped needle.</summary>
    public const string LikeEscapeCharacter = "\\";

    /// <summary>
    /// Escapes LIKE/ILIKE metacharacters in caller text so they match
    /// themselves literally instead of acting as wildcards: "\\", "%" and "_"
    /// are never stripped and never honored. The input is the already
    /// normalized (trimmed, lowercased) search, so no further trimming here.
    /// </summary>
    public static string ToLikePattern(string normalizedSearch) =>
        $"%{normalizedSearch
            .Replace("\\", "\\\\")
            .Replace("%", "\\%")
            .Replace("_", "\\_")}%";

    /// <summary>
    /// The locked authorized-recipient scope: the caregiver's own booking
    /// window — Confirmed/InProgress plus the 30-day completion grace —
    /// evaluated fresh on every picker call, with the name search applied on
    /// the mapped name columns.
    /// </summary>
    public static IQueryable<Elderly> AuthorizedRecipients(
        IFamiliesDbContext dbContext,
        CaregiverId caregiverId,
        DateTime graceCutoffUtc,
        string? normalizedSearch)
    {
        IQueryable<Elderly> dependents = dbContext.Elderlies
            .AsNoTracking()
            .Where(e => dbContext.Bookings.Any(
                b => b.CaregiverId == caregiverId
                    && b.ElderlyId == e.Id
                    && (b.Status == BookingStatus.Confirmed
                        || b.Status == BookingStatus.InProgress
                        || (b.Status == BookingStatus.Completed
                            && b.CompletedOnUtc != null
                            && b.CompletedOnUtc.Value >= graceCutoffUtc))));

        if (string.IsNullOrEmpty(normalizedSearch))
        {
            return dependents;
        }

        string pattern = ToLikePattern(normalizedSearch);

        return dependents.Where(e =>
            EF.Functions.ILike(e.ArabicFullName, pattern, LikeEscapeCharacter) ||
            EF.Functions.ILike(e.EnglishFullName, pattern, LikeEscapeCharacter));
    }

    /// <summary>
    /// One SQL-side page: mapped Arabic-name ordering with the id tie-breaker
    /// for a total order, over the authorized-recipient scope.
    /// </summary>
    public static IQueryable<Elderly> Page(
        IFamiliesDbContext dbContext,
        CaregiverId caregiverId,
        DateTime graceCutoffUtc,
        string? normalizedSearch,
        int page,
        int pageSize) =>
        AuthorizedRecipients(dbContext, caregiverId, graceCutoffUtc, normalizedSearch)
            .OrderBy(e => e.ArabicFullName)
            .ThenBy(e => e.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize);
}

public sealed class ListCaregiverRecipientsQueryHandler
    : IQueryHandler<ListCaregiverRecipientsQuery, CareRecordsPage<CareRecordRecipientResponse>>
{
    private readonly IFamiliesDbContext _dbContext;
    private readonly IDateTimeProvider _dateTimeProvider;

    public ListCaregiverRecipientsQueryHandler(
        IFamiliesDbContext dbContext,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<CareRecordsPage<CareRecordRecipientResponse>>> Handle(
        ListCaregiverRecipientsQuery request,
        CancellationToken cancellationToken)
    {
        var pageQuery = new CareRecordPageQuery(request.Search, request.Page, request.PageSize);
        Error? pageError = pageQuery.Normalize();

        if (pageError is not null)
        {
            return pageError;
        }

        DateTime graceCutoffUtc = _dateTimeProvider.UtcNow
            .AddDays(-CaregiverCareRecordAccess.CompletionGraceDays);

        IQueryable<Elderly> dependents = CaregiverRecipientPickerQuery.AuthorizedRecipients(
            _dbContext, request.CaregiverId, graceCutoffUtc, pageQuery.NormalizedSearch);

        int totalCount = await dependents.CountAsync(cancellationToken);

        List<CareRecordRecipientResponse> items = (await CaregiverRecipientPickerQuery
                .Page(
                    _dbContext,
                    request.CaregiverId,
                    graceCutoffUtc,
                    pageQuery.NormalizedSearch,
                    pageQuery.Page,
                    pageQuery.PageSize)
                .ToListAsync(cancellationToken))
            .Select(e => new CareRecordRecipientResponse(
                e.Id.Value,
                e.ArabicFullName.Value,
                e.EnglishFullName.Value))
            .ToList();

        return CareRecordPageQuery.CreatePage(items, pageQuery.Page, pageQuery.PageSize, totalCount);
    }
}

// ========= Caregiver list: own-source records for one dependent =========

/// <summary>
/// Caregiver read of the Caregiver-<em>source</em> records of one authorized
/// dependent (locked D1/D2 scope is the source dimension, not the author).
/// No cross-source history is ever returned and the booking window is
/// rechecked on every call.
/// </summary>
public sealed record ListCaregiverCareRecordsQuery(
    CaregiverId CaregiverId,
    ElderlyId DependentId,
    DateOnly? VisitDateFrom = null,
    DateOnly? VisitDateTo = null,
    string? Search = null,
    int Page = CareRecordPageSettings.DefaultPage,
    int PageSize = CareRecordPageSettings.DefaultPageSize) : IQuery<CareRecordsPage<CareRecordResponse>>;

public sealed class ListCaregiverCareRecordsQueryHandler
    : IQueryHandler<ListCaregiverCareRecordsQuery, CareRecordsPage<CareRecordResponse>>
{
    private readonly IFamiliesDbContext _dbContext;
    private readonly IDateTimeProvider _dateTimeProvider;

    public ListCaregiverCareRecordsQueryHandler(
        IFamiliesDbContext dbContext,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<CareRecordsPage<CareRecordResponse>>> Handle(
        ListCaregiverCareRecordsQuery request,
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

        if (!await CaregiverCareRecordAccess.HasAuthorizedDependentAsync(
                _dbContext, request.CaregiverId, request.DependentId,
                _dateTimeProvider.UtcNow, cancellationToken))
        {
            return CareRecordErrors.AccessDenied;
        }

        IQueryable<CareRecord> query = _dbContext.CareRecords
            .AsNoTracking()
            .Where(r => r.ElderlyId == request.DependentId
                && r.Source == CareRecordSource.Caregiver);

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

// ==================== Caregiver detail: own source ====================

public sealed record GetCaregiverCareRecordQuery(
    CaregiverId CaregiverId,
    Guid RecordId) : IQuery<CareRecordResponse>;

public sealed class GetCaregiverCareRecordQueryHandler
    : IQueryHandler<GetCaregiverCareRecordQuery, CareRecordResponse>
{
    private readonly IFamiliesDbContext _dbContext;
    private readonly IDateTimeProvider _dateTimeProvider;

    public GetCaregiverCareRecordQueryHandler(
        IFamiliesDbContext dbContext,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<CareRecordResponse>> Handle(
        GetCaregiverCareRecordQuery request,
        CancellationToken cancellationToken)
    {
        CareRecord? record = await _dbContext.CareRecords
            .AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == request.RecordId, cancellationToken);

        if (record is null)
        {
            return CareRecordErrors.NotFound;
        }

        // A known record of another source is a scope denial, never a 404.
        if (record.Source != CareRecordSource.Caregiver)
        {
            return CareRecordErrors.AccessDenied;
        }

        if (!await CaregiverCareRecordAccess.HasAuthorizedDependentAsync(
                _dbContext, request.CaregiverId, record.ElderlyId,
                _dateTimeProvider.UtcNow, cancellationToken))
        {
            return CareRecordErrors.AccessDenied;
        }

        return record.ToResponse();
    }
}

// ==================== Caregiver create (own source) ====================

public sealed record CreateCaregiverCareRecordCommand(
    CaregiverId CaregiverId,
    UserId AuthorUserId,
    ElderlyId DependentId,
    string HospitalName,
    DateOnly VisitDate,
    string Details,
    string CorrelationId,
    IdempotencyRequest? Idempotency = null)
    : ICommand<IdempotentOutcome<CareRecordResponse>>;

public sealed class CreateCaregiverCareRecordCommandValidator
    : AbstractValidator<CreateCaregiverCareRecordCommand>
{
    public CreateCaregiverCareRecordCommandValidator()
    {
        RuleFor(c => c.CaregiverId).NotEqual(CaregiverId.Empty);
        RuleFor(c => c.AuthorUserId).NotEqual(UserId.Empty);
        RuleFor(c => c.DependentId).NotEqual(ElderlyId.Empty);
        RuleFor(c => c.HospitalName).NotEmpty()
            .MaximumLength(CareRecord.MaximumHospitalNameLength);
        RuleFor(c => c.Details).NotEmpty()
            .MaximumLength(CareRecord.MaximumDetailsLength);
        RuleFor(c => c.CorrelationId).NotEmpty();
    }
}

public sealed class CreateCaregiverCareRecordCommandHandler
    : ICommandHandler<CreateCaregiverCareRecordCommand, IdempotentOutcome<CareRecordResponse>>
{
    private static readonly Error IdempotencyConflict = new(
        "Families.CareRecords.IdempotencyConflict",
        "This Idempotency-Key was already used with a different request.");

    private readonly IFamiliesDbContext _dbContext;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IIdempotentResponseSnapshotter _snapshotter;

    public CreateCaregiverCareRecordCommandHandler(
        IFamiliesDbContext dbContext,
        IDateTimeProvider dateTimeProvider,
        IIdempotentResponseSnapshotter snapshotter)
    {
        _dbContext = dbContext;
        _dateTimeProvider = dateTimeProvider;
        _snapshotter = snapshotter;
    }

    public async Task<Result<IdempotentOutcome<CareRecordResponse>>> Handle(
        CreateCaregiverCareRecordCommand request,
        CancellationToken cancellationToken)
    {
        // dependentId on create is a selector, never authority: the booking
        // window is revalidated here at write time even though the picker
        // returned it earlier (locked D1). A replay re-runs this check too, so
        // a retry after the window closed answers AccessDenied: a replay is
        // historical, not a new offer.
        if (!await CaregiverCareRecordAccess.HasAuthorizedDependentAsync(
                _dbContext, request.CaregiverId, request.DependentId,
                _dateTimeProvider.UtcNow, cancellationToken))
        {
            return Result<IdempotentOutcome<CareRecordResponse>>.Failure(
                CareRecordErrors.AccessDenied);
        }

        if (request.Idempotency is not null)
        {
            IdempotencyRecord? stored = await FindStoredAsync(
                request.AuthorUserId,
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

            CareRecord record = CareRecord.CreateForCaregiver(
                request.DependentId,
                request.AuthorUserId,
                request.CaregiverId,
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
                request.AuthorUserId,
                CareRecordAuditAction.Created,
                request.CorrelationId,
                utcNow));

            if (request.Idempotency is not null)
            {
                _dbContext.IdempotencyRequests.Add(
                    IdempotencyRecord.Create(
                        request.AuthorUserId,
                        IdempotencyOperations.CaregiverCareRecordCreate,
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
                request.AuthorUserId,
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
                    row.Operation == IdempotencyOperations.CaregiverCareRecordCreate &&
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

// ==================== Caregiver update (own source) ====================

public sealed record UpdateCaregiverCareRecordCommand(
    CaregiverId CaregiverId,
    UserId AuthorUserId,
    Guid RecordId,
    string HospitalName,
    DateOnly VisitDate,
    string Details,
    int ExpectedVersion,
    string CorrelationId) : ICommand<CareRecordResponse>;

public sealed class UpdateCaregiverCareRecordCommandValidator
    : AbstractValidator<UpdateCaregiverCareRecordCommand>
{
    public UpdateCaregiverCareRecordCommandValidator()
    {
        RuleFor(c => c.CaregiverId).NotEqual(CaregiverId.Empty);
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

public sealed class UpdateCaregiverCareRecordCommandHandler
    : ICommandHandler<UpdateCaregiverCareRecordCommand, CareRecordResponse>
{
    private readonly IFamiliesDbContext _dbContext;
    private readonly IDateTimeProvider _dateTimeProvider;

    public UpdateCaregiverCareRecordCommandHandler(
        IFamiliesDbContext dbContext,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<CareRecordResponse>> Handle(
        UpdateCaregiverCareRecordCommand request,
        CancellationToken cancellationToken)
    {
        CareRecord? record = await _dbContext.CareRecords.SingleOrDefaultAsync(
            r => r.Id == request.RecordId,
            cancellationToken);

        if (record is null)
        {
            return CareRecordErrors.NotFound;
        }

        if (record.Source != CareRecordSource.Caregiver)
        {
            return CareRecordErrors.AccessDenied;
        }

        if (!await CaregiverCareRecordAccess.HasAuthorizedDependentAsync(
                _dbContext, request.CaregiverId, record.ElderlyId,
                _dateTimeProvider.UtcNow, cancellationToken))
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

// ==================== Caregiver delete (own source) ====================

public sealed record DeleteCaregiverCareRecordCommand(
    CaregiverId CaregiverId,
    UserId AuthorUserId,
    Guid RecordId,
    int ExpectedVersion,
    string CorrelationId) : ICommand;

public sealed class DeleteCaregiverCareRecordCommandValidator
    : AbstractValidator<DeleteCaregiverCareRecordCommand>
{
    public DeleteCaregiverCareRecordCommandValidator()
    {
        RuleFor(c => c.CaregiverId).NotEqual(CaregiverId.Empty);
        RuleFor(c => c.AuthorUserId).NotEqual(UserId.Empty);
        RuleFor(c => c.RecordId).NotEqual(Guid.Empty);
        RuleFor(c => c.ExpectedVersion).GreaterThan(0);
        RuleFor(c => c.CorrelationId).NotEmpty();
    }
}

public sealed class DeleteCaregiverCareRecordCommandHandler
    : ICommandHandler<DeleteCaregiverCareRecordCommand>
{
    private readonly IFamiliesDbContext _dbContext;
    private readonly IDateTimeProvider _dateTimeProvider;

    public DeleteCaregiverCareRecordCommandHandler(
        IFamiliesDbContext dbContext,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result> Handle(
        DeleteCaregiverCareRecordCommand request,
        CancellationToken cancellationToken)
    {
        CareRecord? record = await _dbContext.CareRecords.SingleOrDefaultAsync(
            r => r.Id == request.RecordId,
            cancellationToken);

        if (record is null)
        {
            return Result.Failure(CareRecordErrors.NotFound);
        }

        if (record.Source != CareRecordSource.Caregiver)
        {
            return Result.Failure(CareRecordErrors.AccessDenied);
        }

        if (!await CaregiverCareRecordAccess.HasAuthorizedDependentAsync(
                _dbContext, request.CaregiverId, record.ElderlyId,
                _dateTimeProvider.UtcNow, cancellationToken))
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
