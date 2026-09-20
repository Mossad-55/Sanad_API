using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Domain.Reports;

namespace Sanad.Modules.Families.Application.Reports;

public sealed record VisitReportResponse(
    Guid Id,
    Guid BookingId,
    Guid FamilyId,
    Guid ElderlyId,
    Guid CaregiverId,
    BookingCaregiverType CaregiverType,
    string? ObservedCondition,
    string? Activities,
    string? Notes,
    VisitReportAssessment Assessment,
    DateTime StartedOnUtc,
    DateTime CompletedOnUtc,
    DateTime SubmittedOnUtc);

public sealed record SubmitVisitReportCommand(
    CaregiverId CaregiverId,
    UserId CaregiverUserId,
    BookingId BookingId,
    string? ObservedCondition,
    string? Activities,
    string? Notes,
    VisitReportAssessment Assessment,
    DateTime SubmittedOnUtc) : ICommand<VisitReportResponse>;

public sealed class SubmitVisitReportCommandHandler
    : ICommandHandler<SubmitVisitReportCommand, VisitReportResponse>
{
    private readonly IFamiliesDbContext _dbContext;

    public SubmitVisitReportCommandHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<VisitReportResponse>> Handle(
        SubmitVisitReportCommand request,
        CancellationToken cancellationToken)
    {
        Booking? booking = await _dbContext.Bookings
            .AsNoTracking()
            .SingleOrDefaultAsync(
                booking => booking.Id == request.BookingId &&
                           booking.CaregiverId == request.CaregiverId,
                cancellationToken);

        if (booking is null)
        {
            return Result<VisitReportResponse>.Failure(
                new Error("Bookings.NotFound", "Booking not found for this caregiver."));
        }

        if (booking.Status != BookingStatus.Completed ||
            booking.StartedOnUtc is null ||
            booking.CompletedOnUtc is null)
        {
            return Result<VisitReportResponse>.Failure(
                new Error(
                    "Bookings.Domain.InvalidOperation",
                    "A visit report requires a completed booking with attendance timestamps."));
        }

        bool alreadySubmitted = await _dbContext.VisitReports
            .AsNoTracking()
            .AnyAsync(report => report.BookingId == request.BookingId, cancellationToken);

        if (alreadySubmitted)
        {
            return Result<VisitReportResponse>.Failure(VisitReportErrors.AlreadySubmitted);
        }

        try
        {
            VisitReport report = VisitReport.Create(
                booking.Id,
                booking.FamilyId,
                booking.ElderlyId,
                booking.CaregiverId,
                request.CaregiverUserId,
                booking.CaregiverType,
                request.ObservedCondition,
                request.Activities,
                request.Notes,
                request.Assessment,
                booking.StartedOnUtc.Value,
                booking.CompletedOnUtc.Value,
                request.SubmittedOnUtc);

            _dbContext.VisitReports.Add(report);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return Result<VisitReportResponse>.Success(ToResponse(report));
        }
        catch (DomainException)
        {
            return Result<VisitReportResponse>.Failure(VisitReportErrors.InvalidContent);
        }
        catch (DbUpdateException exception) when (VisitReportPersistenceGuard.IsBookingUniqueViolation(exception))
        {
            return Result<VisitReportResponse>.Failure(VisitReportErrors.AlreadySubmitted);
        }
    }

    private static VisitReportResponse ToResponse(VisitReport report) =>
        new(
            report.Id.Value,
            report.BookingId.Value,
            report.FamilyId.Value,
            report.ElderlyId.Value,
            report.CaregiverId.Value,
            report.CaregiverType,
            report.ObservedCondition,
            report.Activities,
            report.Notes,
            report.Assessment,
            report.StartedOnUtc,
            report.CompletedOnUtc,
            report.SubmittedOnUtc);
}

internal static class VisitReportPersistenceGuard
{
    internal static bool IsBookingUniqueViolation(DbUpdateException exception)
    {
        const string constraintName = "ux_visit_reports_booking";

        for (Exception? inner = exception.InnerException;
             inner is not null;
             inner = inner.InnerException)
        {
            if (inner.Message.Contains(constraintName, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
