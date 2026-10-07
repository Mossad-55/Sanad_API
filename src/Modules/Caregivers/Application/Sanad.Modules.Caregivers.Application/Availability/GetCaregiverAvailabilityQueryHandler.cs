using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Microsoft.EntityFrameworkCore;

namespace Sanad.Modules.Caregivers.Application.Availability;

public sealed class GetCaregiverAvailabilityQueryHandler(
    ICaregiversDbContext dbContext) : IQueryHandler<
    GetCaregiverAvailabilityQuery,
    CaregiverAvailabilityResponse>
{
    public async Task<Result<CaregiverAvailabilityResponse>> Handle(
        GetCaregiverAvailabilityQuery request,
        CancellationToken cancellationToken)
    {
        var caregiver = await dbContext.Caregivers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                c => c.Id == new CaregiverId(request.CaregiverId),
                cancellationToken);

        if (caregiver is null)
        {
            return Result<CaregiverAvailabilityResponse>.Failure(
                new Error("Caregivers.NotFound", "Caregiver not found."));
        }

        var userHeader = await dbContext.GetCaregiverUserHeaderAsync(
            caregiver.UserId,
            cancellationToken);

        if (userHeader is null)
        {
            return Result<CaregiverAvailabilityResponse>.Failure(
                new Error("Caregivers.NotFound", "Caregiver identity account not found."));
        }

        var availableSlots = new List<AvailabilitySlot>();

        if (caregiver.CompanionSchedule is not null)
        {
            availableSlots.AddRange(caregiver.CompanionSchedule.Windows.Select(window =>
                new AvailabilitySlot(window.StartTime, window.EndTime, window.DayOfWeek)));
        }

        if (caregiver.MedicalSchedule is not null)
        {
            availableSlots.AddRange(caregiver.MedicalSchedule.Shifts.Select(shift =>
                new AvailabilitySlot(shift.StartTime, shift.EndTime, shift.DayOfWeek)));
            availableSlots.AddRange(caregiver.MedicalSchedule.HomeVisitWindows.Select(window =>
                new AvailabilitySlot(window.StartTime, window.EndTime, window.DayOfWeek)));
        }

        return Result<CaregiverAvailabilityResponse>.Success(
            new CaregiverAvailabilityResponse(
                caregiver.Id.Value,
                userHeader.ArabicFullName,
                userHeader.EnglishFullName,
                null,
                caregiver.Status == CaregiverStatus.Active &&
                caregiver.Availability == CaregiverAvailability.Available
                    ? CaregiverAvailabilityStatus.Active
                    : CaregiverAvailabilityStatus.Inactive,
                null,
                availableSlots));
    }
}
