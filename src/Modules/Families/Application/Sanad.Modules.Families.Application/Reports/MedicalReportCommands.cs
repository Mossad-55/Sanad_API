using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions.Storage;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Domain.Reports;

namespace Sanad.Modules.Families.Application.Reports;

public sealed record SubmitMedicalReportCommand(
    CaregiverId CaregiverId, UserId CaregiverUserId, BookingId BookingId,
    int? Systolic, int? Diastolic, int? Pulse, decimal? Temperature, string? Notes,
    DateTime? MeasurementTakenOnUtc, VisitReportAssessment Assessment,
    bool PhotoConsentConfirmed, DateTime SubmittedOnUtc,
    Stream? PhotoContent, string? PhotoContentType, long PhotoLength) : ICommand<MedicalReportResponse>;

public sealed record MedicalReportResponse(
    Guid Id, Guid BookingId, Guid FamilyId, Guid ElderlyId, Guid CaregiverId,
    BookingCaregiverType CaregiverType, int? Systolic, int? Diastolic, int? Pulse,
    decimal? Temperature, string? Notes, DateTime? MeasurementTakenOnUtc, VisitReportAssessment Assessment,
    DateTime SubmittedOnUtc, bool PhotoAvailable);

public sealed class SubmitMedicalReportCommandHandler : ICommandHandler<SubmitMedicalReportCommand, MedicalReportResponse>
{
    private readonly IFamiliesDbContext _db;
    private readonly IFileStorage _storage;

    public SubmitMedicalReportCommandHandler(IFamiliesDbContext db, IFileStorage storage) { _db = db; _storage = storage; }

    public async Task<Result<MedicalReportResponse>> Handle(SubmitMedicalReportCommand request, CancellationToken cancellationToken)
    {
        Booking? booking = await _db.Bookings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.BookingId && x.CaregiverId == request.CaregiverId, cancellationToken);
        if (booking is null) return Result<MedicalReportResponse>.Failure(new Error("Bookings.NotFound", "Booking not found for this caregiver."));
        if (booking.CaregiverType != BookingCaregiverType.Medical) return Result<MedicalReportResponse>.Failure(MedicalReportErrors.CaregiverForbidden);
        if (booking.Status != BookingStatus.Completed || booking.StartedOnUtc is null || booking.CompletedOnUtc is null)
            return Result<MedicalReportResponse>.Failure(new Error("Bookings.Domain.InvalidOperation", "A medical report requires a completed booking with attendance timestamps."));
        if (await _db.MedicalReports.AsNoTracking().AnyAsync(x => x.BookingId == request.BookingId, cancellationToken))
            return Result<MedicalReportResponse>.Failure(MedicalReportErrors.AlreadySubmitted);

        string? key = null;
        try
        {
            // Validate every request/domain invariant before touching private storage.
            _ = MedicalReport.Create(booking.Id, booking.FamilyId, booking.ElderlyId, booking.CaregiverId,
                request.CaregiverUserId, booking.CaregiverType, request.Systolic, request.Diastolic, request.Pulse,
                request.Temperature, request.Notes, request.MeasurementTakenOnUtc, request.Assessment, request.SubmittedOnUtc,
                request.PhotoConsentConfirmed, request.SubmittedOnUtc, request.CaregiverUserId, null);
            if (request.PhotoContent is not null)
            {
                Result<StoredFile> saved = await _storage.SavePrivateAsync(request.PhotoContent, request.PhotoContentType ?? string.Empty, request.PhotoLength, "medical-reports", cancellationToken);
                if (saved.IsFailure) return Result<MedicalReportResponse>.Failure(saved.Error);
                key = saved.Value.Key;
            }
            MedicalReport report = MedicalReport.Create(booking.Id, booking.FamilyId, booking.ElderlyId, booking.CaregiverId,
                request.CaregiverUserId, booking.CaregiverType, request.Systolic, request.Diastolic, request.Pulse,
                request.Temperature, request.Notes, request.MeasurementTakenOnUtc, request.Assessment, request.SubmittedOnUtc,
                request.PhotoConsentConfirmed, request.SubmittedOnUtc, request.CaregiverUserId, key);
            _db.MedicalReports.Add(report);
            await _db.SaveChangesAsync(cancellationToken);
            return Result<MedicalReportResponse>.Success(ToResponse(report));
        }
        catch (DomainException) { if (key is not null) await _storage.DeleteAsync(key, CancellationToken.None); return Result<MedicalReportResponse>.Failure(MedicalReportErrors.InvalidContent); }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception)) { if (key is not null) await _storage.DeleteAsync(key, CancellationToken.None); return Result<MedicalReportResponse>.Failure(MedicalReportErrors.AlreadySubmitted); }
        catch { if (key is not null) await _storage.DeleteAsync(key, CancellationToken.None); throw; }
    }

    internal static MedicalReportResponse ToResponse(MedicalReport x) => new(x.Id.Value, x.BookingId.Value, x.FamilyId.Value, x.ElderlyId.Value, x.CaregiverId.Value, x.CaregiverType, x.Systolic, x.Diastolic, x.Pulse, x.Temperature, x.Notes, x.MeasurementTakenOnUtc, x.Assessment, x.SubmittedOnUtc, x.PhotoKey is not null);
    private static bool IsUniqueViolation(DbUpdateException exception) => exception.ToString().Contains("ux_medical_reports_booking", StringComparison.Ordinal);
}
