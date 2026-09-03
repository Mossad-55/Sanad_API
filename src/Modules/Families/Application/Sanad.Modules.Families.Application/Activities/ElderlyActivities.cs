using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Application.Families;
using Sanad.Modules.Families.Domain.Activities;
using Sanad.Modules.Families.Domain.Families;

namespace Sanad.Modules.Families.Application.Activities;

public sealed record ElderlyActivityItemResponse(
    Guid Id,
    Guid DependentId,
    Guid ActorUserId,
    ElderlyActivityType ActivityType,
    string ActivityTypeNameAr,
    string ActivityTypeNameEn,
    string Summary,
    DateTime CreatedOnUtc);

public sealed record ElderlyActivityDashboardResponse(
    int TotalEventsCount,
    int ThisWeekEventsCount,
    int UniqueUsersCount,
    IReadOnlyList<ElderlyActivityItemResponse> Activities);

internal static class ElderlyActivityMappings
{
    public static ElderlyActivityItemResponse ToResponse(this ElderlyActivityLog log) =>
        new(
            log.Id.Value,
            log.ElderlyId.Value,
            log.ActorUserId.Value,
            log.ActivityType,
            GetActivityNameAr(log.ActivityType),
            GetActivityNameEn(log.ActivityType),
            log.Summary,
            log.CreatedOnUtc);

    public static string GetActivityNameAr(ElderlyActivityType type) => type switch
    {
        ElderlyActivityType.ViewMedicalProfile => "عرض الملف الطبي",
        ElderlyActivityType.UpdateMedications => "تحديث الأدوية",
        ElderlyActivityType.AddNote => "إضافة ملاحظة",
        ElderlyActivityType.ShareMedicalProfile => "مشاركة الملف الطبي",
        ElderlyActivityType.ScheduleAppointment => "تحديد موعد",
        ElderlyActivityType.ReviewMedications => "مراجعة الأدوية",
        _ => "نشاط"
    };

    public static string GetActivityNameEn(ElderlyActivityType type) => type switch
    {
        ElderlyActivityType.ViewMedicalProfile => "Viewed Medical Profile",
        ElderlyActivityType.UpdateMedications => "Updated Medications",
        ElderlyActivityType.AddNote => "Added Care Note",
        ElderlyActivityType.ShareMedicalProfile => "Shared Medical Profile",
        ElderlyActivityType.ScheduleAppointment => "Scheduled Appointment",
        ElderlyActivityType.ReviewMedications => "Reviewed Medications",
        _ => "Activity"
    };
}

public sealed record GetElderlyActivityTimelineQuery(
    UserId UserId,
    ElderlyId DependentId,
    int Limit = 50) : IQuery<ElderlyActivityDashboardResponse>;

public sealed class GetElderlyActivityTimelineQueryHandler : IQueryHandler<GetElderlyActivityTimelineQuery, ElderlyActivityDashboardResponse>
{
    private readonly IFamiliesDbContext _dbContext;

    public GetElderlyActivityTimelineQueryHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<ElderlyActivityDashboardResponse>> Handle(GetElderlyActivityTimelineQuery request, CancellationToken cancellationToken)
    {
        Family? family = await FamilyAccess.ResolveFamilyAsync(_dbContext, request.UserId, cancellationToken);
        if (family is null || !FamilyAccess.CanManage(family, request.UserId))
        {
            return FamilyErrors.AccessDenied;
        }

        DateTime weekAgoUtc = DateTime.UtcNow.AddDays(-7);

        var baseQuery = _dbContext.ElderlyActivityLogs
            .AsNoTracking()
            .Where(l => l.ElderlyId == request.DependentId);

        int totalEvents = await baseQuery.CountAsync(cancellationToken);
        int thisWeekEvents = await baseQuery.CountAsync(l => l.CreatedOnUtc >= weekAgoUtc, cancellationToken);
        int uniqueUsers = await baseQuery.Select(l => l.ActorUserId).Distinct().CountAsync(cancellationToken);

        List<ElderlyActivityLog> logs = await baseQuery
            .OrderByDescending(l => l.CreatedOnUtc)
            .Take(Math.Max(1, Math.Min(request.Limit, 100)))
            .ToListAsync(cancellationToken);

        var response = new ElderlyActivityDashboardResponse(
            totalEvents,
            thisWeekEvents,
            uniqueUsers,
            logs.Select(l => l.ToResponse()).ToList());

        return Result<ElderlyActivityDashboardResponse>.Success(response);
    }
}