using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Cms.Application.Abstractions.Data;
using Sanad.Modules.Cms.Domain.MedicationLateness;

namespace Sanad.Modules.Cms.Application.MedicationLateness;

public sealed record MedicationLatenessSettingResponse(Guid RevisionId, int Version, int ThresholdMinutes, bool IsActive, DateTime CreatedOnUtc);
public sealed record GetMedicationLatenessSettingQuery : IQuery<MedicationLatenessSettingResponse>;
public sealed record CreateMedicationLatenessSettingRevisionCommand(int ThresholdMinutes) : ICommand<MedicationLatenessSettingResponse>;

public static class MedicationLatenessSettingErrors
{
    public static readonly Error NotFound = new("Cms.MedicationLateness.NotFound", "The medication lateness setting is not configured.");
    public static readonly Error Invalid = new("Cms.MedicationLateness.Invalid", "The medication lateness threshold is invalid.");
}

internal static class MedicationLatenessSettingMapping
{
    public static MedicationLatenessSettingResponse Map(MedicationLatenessSettingRevision revision) =>
        new(revision.Id, revision.Version, revision.ThresholdMinutes, revision.IsActive, revision.CreatedOnUtc);
}

public sealed class GetMedicationLatenessSettingQueryHandler(ICmsDbContext db) : IQueryHandler<GetMedicationLatenessSettingQuery, MedicationLatenessSettingResponse>
{
    public async Task<Result<MedicationLatenessSettingResponse>> Handle(GetMedicationLatenessSettingQuery request, CancellationToken ct)
    {
        var revision = await db.MedicationLatenessSettingRevisions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.IsActive, ct);
        return revision is null ? MedicationLatenessSettingErrors.NotFound : MedicationLatenessSettingMapping.Map(revision);
    }
}

public sealed class CreateMedicationLatenessSettingRevisionCommandHandler(ICmsDbContext db) : ICommandHandler<CreateMedicationLatenessSettingRevisionCommand, MedicationLatenessSettingResponse>
{
    public async Task<Result<MedicationLatenessSettingResponse>> Handle(CreateMedicationLatenessSettingRevisionCommand request, CancellationToken ct)
    {
        if (request.ThresholdMinutes is < MedicationLatenessSetting.MinimumThresholdMinutes or > MedicationLatenessSetting.MaximumThresholdMinutes)
            return MedicationLatenessSettingErrors.Invalid;

        var setting = await db.MedicationLatenessSettings.Include(x => x.Revisions).SingleOrDefaultAsync(ct);
        setting ??= MedicationLatenessSetting.Create();
        foreach (var active in setting.Revisions.Where(x => x.IsActive)) active.Deactivate();
        var revision = setting.AddRevision(request.ThresholdMinutes);
        revision.Activate();
        if (setting.Revisions.Count == 1) db.MedicationLatenessSettings.Add(setting);
        try
        {
            await db.SaveChangesAsync(ct);
            return MedicationLatenessSettingMapping.Map(revision);
        }
        catch (DbUpdateException)
        {
            return MedicationLatenessSettingErrors.Invalid;
        }
    }
}
