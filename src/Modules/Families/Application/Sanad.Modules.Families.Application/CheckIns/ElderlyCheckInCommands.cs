using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Application.Abstractions.Notifications;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Elderlies.CheckIns;

namespace Sanad.Modules.Families.Application.CheckIns;

public sealed record SubmitElderlyCheckInCommand(UserId ActorUserId, bool Answer) : ICommand<ElderlyCheckInResponse>;
public sealed record ElderlyCheckInResponse(Guid Id, Guid ElderlyId, bool Answer, DateOnly LocalDate, TimeOnly AnsweredAtLocalTime, DateTime AnsweredOnUtc, string TimeZoneId);

public sealed class SubmitElderlyCheckInCommandHandler(IFamiliesDbContext db, IElderlyCheckInAlertGateway alerts)
    : ICommandHandler<SubmitElderlyCheckInCommand, ElderlyCheckInResponse>
{
    private static readonly Error InvalidTimeZone = new("Families.ElderlyCheckIn.InvalidTimeZone", "The elderly profile has an invalid time zone.");
    private static readonly Error AlreadyAnswered = new("Families.ElderlyCheckIn.AlreadyAnswered", "The daily check-in has already been answered with a different answer.");
    private static readonly Error NotFound = new("Families.ElderlyCheckIn.NotFound", "The elderly profile was not found.");

    public async Task<Result<ElderlyCheckInResponse>> Handle(SubmitElderlyCheckInCommand request, CancellationToken ct)
    {
        var elderly = await db.Elderlies.SingleOrDefaultAsync(x => x.IdentityUserId == request.ActorUserId, ct);
        if (elderly is null || !await db.Families.AnyAsync(x => x.Id == elderly.FamilyId && x.DeletedOnUtc == null, ct))
            return Result<ElderlyCheckInResponse>.Failure(NotFound);

        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(ElderlyTimeZone.Normalize(elderly.TimeZoneId)); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or Sanad.BuildingBlocks.Domain.Exceptions.DomainException)
        { return Result<ElderlyCheckInResponse>.Failure(InvalidTimeZone); }

        var nowUtc = DateTime.UtcNow;
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone);
        var localDate = DateOnly.FromDateTime(localNow);
        var existing = await db.ElderlyCheckIns.SingleOrDefaultAsync(x => x.ElderlyId == elderly.Id && x.LocalDate == localDate, ct);
        if (existing is not null)
        {
            if (existing.Answer != request.Answer) return Result<ElderlyCheckInResponse>.Failure(AlreadyAnswered);
            if (!existing.Answer)
                await alerts.CreateNegativeCheckInAlertsAsync(new(elderly.IdentityUserId.Value, elderly.Id.Value, existing.LocalDate), existing.AnsweredOnUtc, ct);
            return ToResponse(existing, elderly.TimeZoneId);
        }

        var checkIn = ElderlyCheckIn.Create(elderly.Id, localDate, request.Answer, TimeOnly.FromDateTime(localNow), nowUtc, request.ActorUserId);
        db.ElderlyCheckIns.Add(checkIn);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            var raced = await db.ElderlyCheckIns.AsNoTracking().SingleOrDefaultAsync(x => x.ElderlyId == elderly.Id && x.LocalDate == localDate, ct);
            if (raced is not null && raced.Answer == request.Answer) return ToResponse(raced, elderly.TimeZoneId);
            if (raced is not null) return Result<ElderlyCheckInResponse>.Failure(AlreadyAnswered);
            throw;
        }

        // Families and Notifications have separate persistence boundaries. Only the insert winner fans out.
        if (!request.Answer)
            await alerts.CreateNegativeCheckInAlertsAsync(new(elderly.IdentityUserId.Value, elderly.Id.Value, localDate), nowUtc, ct);
        return ToResponse(checkIn, elderly.TimeZoneId);
    }

    private static ElderlyCheckInResponse ToResponse(ElderlyCheckIn x, string zone) => new(x.Id, x.ElderlyId.Value, x.Answer, x.LocalDate, x.AnsweredAtLocalTime, x.AnsweredOnUtc, zone);
}
