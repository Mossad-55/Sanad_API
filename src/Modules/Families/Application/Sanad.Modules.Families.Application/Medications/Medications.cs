using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Application.Families;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Domain.Medications;

namespace Sanad.Modules.Families.Application.Medications;

// ================================= Responses =================================

public sealed record MedicationResponse(
    Guid Id,
    Guid DependentId,
    string Name,
    string Dosage,
    string DoseUnit,
    int DoseQuantity,
    IReadOnlyList<TimeOnly> DoseTimes,
    DateOnly StartDate,
    DateOnly? EndDate,
    string? Instructions,
    int? StockQuantity,
    int? LowStockThreshold,
    StockStatus StockStatus,
    MedicationStatus Status,
    DateTime CreatedOnUtc,
    DateTime UpdatedOnUtc);

public sealed record MedicationDoseResponse(
    Guid? DoseLogId,
    Guid MedicationId,
    string MedicationName,
    string Dosage,
    string DoseUnit,
    int DoseQuantity,
    string? Instructions,
    DateOnly ScheduledDate,
    TimeOnly ScheduledTime,
    DoseStatus Status,
    DateTime? TakenAtUtc,
    DateTime? SkippedAtUtc,
    string? Notes,
    Guid? LoggedByUserId);

public sealed record MedicationDashboardResponse(
    int ActiveMedicationsCount,
    int LowStockMedicationsCount,
    int TotalDosesToday,
    int TakenDosesToday,
    int RemainingDosesToday,
    IReadOnlyList<MedicationDoseResponse> TodayDoses,
    IReadOnlyList<MedicationResponse> LowStockAlerts);

internal static class MedicationMappings
{
    public static MedicationResponse ToResponse(this Medication med) =>
        new(
            med.Id.Value,
            med.ElderlyId.Value,
            med.Name,
            med.Dosage,
            med.DoseUnit,
            med.DoseQuantity,
            med.DoseTimes,
            med.StartDate,
            med.EndDate,
            med.Instructions,
            med.StockQuantity,
            med.LowStockThreshold,
            med.GetStockStatus(),
            med.Status,
            med.CreatedOnUtc,
            med.UpdatedOnUtc);
}

// ============================== Add Medication ==============================

public sealed record AddMedicationCommand(
    UserId UserId,
    ElderlyId DependentId,
    string Name,
    string Dosage,
    string DoseUnit,
    int DoseQuantity,
    IReadOnlyList<TimeOnly> DoseTimes,
    DateOnly StartDate,
    DateOnly? EndDate,
    string? Instructions,
    int? StockQuantity,
    int? LowStockThreshold) : ICommand<MedicationResponse>;

public sealed class AddMedicationCommandValidator : AbstractValidator<AddMedicationCommand>
{
    public AddMedicationCommandValidator()
    {
        RuleFor(c => c.UserId).NotEqual(UserId.Empty);
        RuleFor(c => c.DependentId).NotEqual(ElderlyId.Empty);
        RuleFor(c => c.Name).NotEmpty().MaximumLength(Medication.MaximumNameLength);
        RuleFor(c => c.Dosage).NotEmpty().MaximumLength(Medication.MaximumDosageLength);
        RuleFor(c => c.DoseUnit).NotEmpty().MaximumLength(Medication.MaximumDoseUnitLength);
        RuleFor(c => c.DoseQuantity).GreaterThan(0);
        RuleFor(c => c.DoseTimes).NotEmpty().WithMessage("At least one dose time is required.");
        RuleFor(c => c.Instructions).MaximumLength(Medication.MaximumInstructionsLength).When(c => c.Instructions is not null);
        RuleFor(c => c.StockQuantity).GreaterThanOrEqualTo(0).When(c => c.StockQuantity.HasValue);
        RuleFor(c => c.LowStockThreshold).GreaterThanOrEqualTo(0).When(c => c.LowStockThreshold.HasValue);
    }
}

public sealed class AddMedicationCommandHandler : ICommandHandler<AddMedicationCommand, MedicationResponse>
{
    private readonly IFamiliesDbContext _dbContext;

    public AddMedicationCommandHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<MedicationResponse>> Handle(AddMedicationCommand request, CancellationToken cancellationToken)
    {
        Family? family = await FamilyAccess.ResolveFamilyAsync(_dbContext, request.UserId, cancellationToken);
        if (family is null) return MedicationErrors.AccessDenied;
        if (!FamilyAccess.CanManage(family, request.UserId)) return MedicationErrors.AccessDenied;

        bool dependentExists = await _dbContext.Elderlies.AnyAsync(
            e => e.Id == request.DependentId && e.FamilyId == family.Id,
            cancellationToken);

        if (!dependentExists) return MedicationErrors.DependentNotFound;

        try
        {
            var medication = Medication.Create(
                request.DependentId,
                request.UserId,
                request.Name,
                request.Dosage,
                request.DoseUnit,
                request.DoseQuantity,
                request.DoseTimes,
                request.StartDate,
                request.EndDate,
                request.Instructions,
                request.StockQuantity,
                request.LowStockThreshold);

            _dbContext.Medications.Add(medication);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return medication.ToResponse();
        }
        catch (DomainException)
        {
            return MedicationErrors.InvalidMedication;
        }
    }
}

// ============================= Update Medication =============================

public sealed record UpdateMedicationCommand(
    UserId UserId,
    ElderlyId DependentId,
    MedicationId MedicationId,
    string Name,
    string Dosage,
    string DoseUnit,
    int DoseQuantity,
    IReadOnlyList<TimeOnly> DoseTimes,
    DateOnly StartDate,
    DateOnly? EndDate,
    string? Instructions) : ICommand<MedicationResponse>;

public sealed class UpdateMedicationCommandValidator : AbstractValidator<UpdateMedicationCommand>
{
    public UpdateMedicationCommandValidator()
    {
        RuleFor(c => c.UserId).NotEqual(UserId.Empty);
        RuleFor(c => c.DependentId).NotEqual(ElderlyId.Empty);
        RuleFor(c => c.MedicationId).NotEqual(MedicationId.Empty);
        RuleFor(c => c.Name).NotEmpty().MaximumLength(Medication.MaximumNameLength);
        RuleFor(c => c.Dosage).NotEmpty().MaximumLength(Medication.MaximumDosageLength);
        RuleFor(c => c.DoseUnit).NotEmpty().MaximumLength(Medication.MaximumDoseUnitLength);
        RuleFor(c => c.DoseQuantity).GreaterThan(0);
        RuleFor(c => c.DoseTimes).NotEmpty();
    }
}

public sealed class UpdateMedicationCommandHandler : ICommandHandler<UpdateMedicationCommand, MedicationResponse>
{
    private readonly IFamiliesDbContext _dbContext;

    public UpdateMedicationCommandHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<MedicationResponse>> Handle(UpdateMedicationCommand request, CancellationToken cancellationToken)
    {
        Family? family = await FamilyAccess.ResolveFamilyAsync(_dbContext, request.UserId, cancellationToken);
        if (family is null || !FamilyAccess.CanManage(family, request.UserId)) return MedicationErrors.AccessDenied;

        Medication? medication = await _dbContext.Medications.SingleOrDefaultAsync(
            m => m.Id == request.MedicationId && m.ElderlyId == request.DependentId,
            cancellationToken);

        if (medication is null) return MedicationErrors.NotFound;

        try
        {
            medication.UpdateDetails(
                request.Name,
                request.Dosage,
                request.DoseUnit,
                request.DoseQuantity,
                request.DoseTimes,
                request.StartDate,
                request.EndDate,
                request.Instructions);

            await _dbContext.SaveChangesAsync(cancellationToken);
            return medication.ToResponse();
        }
        catch (DomainException)
        {
            return MedicationErrors.InvalidMedication;
        }
    }
}

// ============================ Update Stock ============================

public sealed record UpdateMedicationStockCommand(
    UserId UserId,
    ElderlyId DependentId,
    MedicationId MedicationId,
    int? StockQuantity,
    int? LowStockThreshold) : ICommand<MedicationResponse>;

public sealed class UpdateMedicationStockCommandHandler : ICommandHandler<UpdateMedicationStockCommand, MedicationResponse>
{
    private readonly IFamiliesDbContext _dbContext;

    public UpdateMedicationStockCommandHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<MedicationResponse>> Handle(UpdateMedicationStockCommand request, CancellationToken cancellationToken)
    {
        Family? family = await FamilyAccess.ResolveFamilyAsync(_dbContext, request.UserId, cancellationToken);
        if (family is null || !FamilyAccess.CanManage(family, request.UserId)) return MedicationErrors.AccessDenied;

        Medication? medication = await _dbContext.Medications.SingleOrDefaultAsync(
            m => m.Id == request.MedicationId && m.ElderlyId == request.DependentId,
            cancellationToken);

        if (medication is null) return MedicationErrors.NotFound;

        try
        {
            medication.UpdateStock(request.StockQuantity, request.LowStockThreshold);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return medication.ToResponse();
        }
        catch (DomainException)
        {
            return MedicationErrors.InvalidMedication;
        }
    }
}

// ======================== Status Toggle Commands ========================

public sealed record PauseMedicationCommand(UserId UserId, ElderlyId DependentId, MedicationId MedicationId) : ICommand<MedicationResponse>;
public sealed record ResumeMedicationCommand(UserId UserId, ElderlyId DependentId, MedicationId MedicationId) : ICommand<MedicationResponse>;
public sealed record DiscontinueMedicationCommand(UserId UserId, ElderlyId DependentId, MedicationId MedicationId) : ICommand<MedicationResponse>;

public sealed class StatusToggleCommandHandlers :
    ICommandHandler<PauseMedicationCommand, MedicationResponse>,
    ICommandHandler<ResumeMedicationCommand, MedicationResponse>,
    ICommandHandler<DiscontinueMedicationCommand, MedicationResponse>
{
    private readonly IFamiliesDbContext _dbContext;

    public StatusToggleCommandHandlers(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<MedicationResponse>> Handle(PauseMedicationCommand request, CancellationToken cancellationToken)
    {
        return await ExecuteToggle(request.UserId, request.DependentId, request.MedicationId, m => m.Pause(), cancellationToken);
    }

    public async Task<Result<MedicationResponse>> Handle(ResumeMedicationCommand request, CancellationToken cancellationToken)
    {
        return await ExecuteToggle(request.UserId, request.DependentId, request.MedicationId, m => m.Resume(), cancellationToken);
    }

    public async Task<Result<MedicationResponse>> Handle(DiscontinueMedicationCommand request, CancellationToken cancellationToken)
    {
        return await ExecuteToggle(request.UserId, request.DependentId, request.MedicationId, m => m.Discontinue(), cancellationToken);
    }

    private async Task<Result<MedicationResponse>> ExecuteToggle(
        UserId userId, ElderlyId depId, MedicationId medId, Action<Medication> action, CancellationToken ct)
    {
        Family? family = await FamilyAccess.ResolveFamilyAsync(_dbContext, userId, ct);
        if (family is null || !FamilyAccess.CanManage(family, userId)) return MedicationErrors.AccessDenied;

        Medication? med = await _dbContext.Medications.SingleOrDefaultAsync(
            m => m.Id == medId && m.ElderlyId == depId, ct);

        if (med is null) return MedicationErrors.NotFound;

        try
        {
            action(med);
            await _dbContext.SaveChangesAsync(ct);
            return med.ToResponse();
        }
        catch (DomainException)
        {
            return MedicationErrors.InvalidMedication;
        }
    }
}

// ============================ Query Endpoints ============================

public sealed record GetMedicationByIdQuery(UserId UserId, ElderlyId DependentId, MedicationId MedicationId) : IQuery<MedicationResponse>;

public sealed class GetMedicationByIdQueryHandler : IQueryHandler<GetMedicationByIdQuery, MedicationResponse>
{
    private readonly IFamiliesDbContext _dbContext;

    public GetMedicationByIdQueryHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<MedicationResponse>> Handle(GetMedicationByIdQuery request, CancellationToken cancellationToken)
    {
        Family? family = await FamilyAccess.ResolveFamilyAsync(_dbContext, request.UserId, cancellationToken);
        if (family is null) return MedicationErrors.AccessDenied;

        Medication? med = await _dbContext.Medications
            .AsNoTracking()
            .SingleOrDefaultAsync(m => m.Id == request.MedicationId && m.ElderlyId == request.DependentId, cancellationToken);

        if (med is null) return MedicationErrors.NotFound;

        return med.ToResponse();
    }
}

public sealed record ListMedicationsQuery(UserId UserId, ElderlyId DependentId) : IQuery<IReadOnlyList<MedicationResponse>>;

public sealed class ListMedicationsQueryHandler : IQueryHandler<ListMedicationsQuery, IReadOnlyList<MedicationResponse>>
{
    private readonly IFamiliesDbContext _dbContext;

    public ListMedicationsQueryHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<IReadOnlyList<MedicationResponse>>> Handle(ListMedicationsQuery request, CancellationToken cancellationToken)
    {
        Family? family = await FamilyAccess.ResolveFamilyAsync(_dbContext, request.UserId, cancellationToken);
        if (family is null) return MedicationErrors.AccessDenied;

        List<Medication> list = await _dbContext.Medications
            .AsNoTracking()
            .Where(m => m.ElderlyId == request.DependentId)
            .OrderByDescending(m => m.CreatedOnUtc)
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<MedicationResponse>>.Success(list.Select(m => m.ToResponse()).ToList());
    }
}

// ======================= Daily Dashboard Query =======================

public sealed record GetMedicationDashboardQuery(
    UserId UserId,
    ElderlyId DependentId,
    DateOnly Date) : IQuery<MedicationDashboardResponse>;

public sealed class GetMedicationDashboardQueryHandler : IQueryHandler<GetMedicationDashboardQuery, MedicationDashboardResponse>
{
    private readonly IFamiliesDbContext _dbContext;

    public GetMedicationDashboardQueryHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<MedicationDashboardResponse>> Handle(GetMedicationDashboardQuery request, CancellationToken cancellationToken)
    {
        Family? family = await FamilyAccess.ResolveFamilyAsync(_dbContext, request.UserId, cancellationToken);
        if (family is null) return MedicationErrors.AccessDenied;

        List<Medication> allMedications = await _dbContext.Medications
            .AsNoTracking()
            .Where(m => m.ElderlyId == request.DependentId)
            .ToListAsync(cancellationToken);

        var activeMedications = allMedications
            .Where(m => m.Status == MedicationStatus.Active &&
                        m.StartDate <= request.Date &&
                        (!m.EndDate.HasValue || m.EndDate.Value >= request.Date))
            .ToList();

        List<MedicationDoseLog> existingLogs = await _dbContext.MedicationDoseLogs
            .AsNoTracking()
            .Where(l => l.ElderlyId == request.DependentId && l.ScheduledDate == request.Date)
            .ToListAsync(cancellationToken);

        var todayDoses = new List<MedicationDoseResponse>();

        foreach (var med in activeMedications)
        {
            foreach (var time in med.DoseTimes)
            {
                var logged = existingLogs.FirstOrDefault(l => l.MedicationId == med.Id && l.ScheduledTime == time);

                if (logged is not null)
                {
                    todayDoses.Add(new MedicationDoseResponse(
                        logged.Id.Value,
                        med.Id.Value,
                        med.Name,
                        med.Dosage,
                        med.DoseUnit,
                        med.DoseQuantity,
                        med.Instructions,
                        logged.ScheduledDate,
                        logged.ScheduledTime,
                        logged.Status,
                        logged.TakenAtUtc,
                        logged.SkippedAtUtc,
                        logged.Notes,
                        logged.LoggedByUserId?.Value));
                }
                else
                {
                    todayDoses.Add(new MedicationDoseResponse(
                        null,
                        med.Id.Value,
                        med.Name,
                        med.Dosage,
                        med.DoseUnit,
                        med.DoseQuantity,
                        med.Instructions,
                        request.Date,
                        time,
                        DoseStatus.Scheduled,
                        null,
                        null,
                        null,
                        null));
                }
            }
        }

        todayDoses = todayDoses.OrderBy(d => d.ScheduledTime).ToList();

        var lowStockAlerts = allMedications
            .Where(m => m.GetStockStatus() == StockStatus.LowStock || m.GetStockStatus() == StockStatus.OutOfStock)
            .Select(m => m.ToResponse())
            .ToList();

        int takenCount = todayDoses.Count(d => d.Status == DoseStatus.Taken);
        int totalDoses = todayDoses.Count;
        int remainingCount = todayDoses.Count(d => d.Status == DoseStatus.Scheduled);

        var dashboard = new MedicationDashboardResponse(
            activeMedications.Count,
            lowStockAlerts.Count,
            totalDoses,
            takenCount,
            remainingCount,
            todayDoses,
            lowStockAlerts);

        return Result<MedicationDashboardResponse>.Success(dashboard);
    }
}

// ========================== Dose Action Commands ==========================

public sealed record RecordDoseTakenCommand(
    UserId UserId,
    ElderlyId DependentId,
    MedicationId MedicationId,
    DateOnly ScheduledDate,
    TimeOnly ScheduledTime,
    string? Notes,
    DateTime UtcNow) : ICommand<MedicationDoseResponse>;

public sealed class RecordDoseTakenCommandHandler : ICommandHandler<RecordDoseTakenCommand, MedicationDoseResponse>
{
    private readonly IFamiliesDbContext _dbContext;

    public RecordDoseTakenCommandHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<MedicationDoseResponse>> Handle(RecordDoseTakenCommand request, CancellationToken cancellationToken)
    {
        Family? family = await FamilyAccess.ResolveFamilyAsync(_dbContext, request.UserId, cancellationToken);
        if (family is null || !FamilyAccess.CanManage(family, request.UserId)) return MedicationErrors.AccessDenied;

        Medication? medication = await _dbContext.Medications.SingleOrDefaultAsync(
            m => m.Id == request.MedicationId && m.ElderlyId == request.DependentId,
            cancellationToken);

        if (medication is null) return MedicationErrors.NotFound;

        MedicationDoseLog? doseLog = await _dbContext.MedicationDoseLogs.SingleOrDefaultAsync(
            l => l.MedicationId == request.MedicationId &&
                 l.ElderlyId == request.DependentId &&
                 l.ScheduledDate == request.ScheduledDate &&
                 l.ScheduledTime == request.ScheduledTime,
            cancellationToken);

        if (doseLog is null)
        {
            doseLog = MedicationDoseLog.CreateScheduled(
                request.MedicationId,
                request.DependentId,
                request.ScheduledDate,
                request.ScheduledTime);

            _dbContext.MedicationDoseLogs.Add(doseLog);
        }

        if (doseLog.Status == DoseStatus.Taken)
        {
            return MedicationErrors.DoseAlreadyTaken;
        }

        doseLog.MarkAsTaken(request.UserId, request.UtcNow, request.Notes);

        // Auto decrement stock
        medication.DecrementStock(medication.DoseQuantity);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new MedicationDoseResponse(
            doseLog.Id.Value,
            medication.Id.Value,
            medication.Name,
            medication.Dosage,
            medication.DoseUnit,
            medication.DoseQuantity,
            medication.Instructions,
            doseLog.ScheduledDate,
            doseLog.ScheduledTime,
            doseLog.Status,
            doseLog.TakenAtUtc,
            doseLog.SkippedAtUtc,
            doseLog.Notes,
            doseLog.LoggedByUserId?.Value);
    }
}

public sealed record RecordDoseSkippedCommand(
    UserId UserId,
    ElderlyId DependentId,
    MedicationId MedicationId,
    DateOnly ScheduledDate,
    TimeOnly ScheduledTime,
    string? Reason,
    DateTime UtcNow) : ICommand<MedicationDoseResponse>;

public sealed class RecordDoseSkippedCommandHandler : ICommandHandler<RecordDoseSkippedCommand, MedicationDoseResponse>
{
    private readonly IFamiliesDbContext _dbContext;

    public RecordDoseSkippedCommandHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<MedicationDoseResponse>> Handle(RecordDoseSkippedCommand request, CancellationToken cancellationToken)
    {
        Family? family = await FamilyAccess.ResolveFamilyAsync(_dbContext, request.UserId, cancellationToken);
        if (family is null || !FamilyAccess.CanManage(family, request.UserId)) return MedicationErrors.AccessDenied;

        Medication? medication = await _dbContext.Medications.SingleOrDefaultAsync(
            m => m.Id == request.MedicationId && m.ElderlyId == request.DependentId,
            cancellationToken);

        if (medication is null) return MedicationErrors.NotFound;

        MedicationDoseLog? doseLog = await _dbContext.MedicationDoseLogs.SingleOrDefaultAsync(
            l => l.MedicationId == request.MedicationId &&
                 l.ElderlyId == request.DependentId &&
                 l.ScheduledDate == request.ScheduledDate &&
                 l.ScheduledTime == request.ScheduledTime,
            cancellationToken);

        if (doseLog is null)
        {
            doseLog = MedicationDoseLog.CreateScheduled(
                request.MedicationId,
                request.DependentId,
                request.ScheduledDate,
                request.ScheduledTime);

            _dbContext.MedicationDoseLogs.Add(doseLog);
        }

        doseLog.MarkAsSkipped(request.UserId, request.UtcNow, request.Reason);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new MedicationDoseResponse(
            doseLog.Id.Value,
            medication.Id.Value,
            medication.Name,
            medication.Dosage,
            medication.DoseUnit,
            medication.DoseQuantity,
            medication.Instructions,
            doseLog.ScheduledDate,
            doseLog.ScheduledTime,
            doseLog.Status,
            doseLog.TakenAtUtc,
            doseLog.SkippedAtUtc,
            doseLog.Notes,
            doseLog.LoggedByUserId?.Value);
    }
}