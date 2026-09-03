using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Families.Domain.Activities;

public sealed class ElderlyActivityLog : Entity<ElderlyActivityLogId>
{
    public const int MaximumSummaryLength = 200;

    private ElderlyActivityLog()
    {
    }

    private ElderlyActivityLog(
        ElderlyActivityLogId id,
        ElderlyId elderlyId,
        UserId actorUserId,
        ElderlyActivityType activityType,
        string summary)
        : base(id)
    {
        ElderlyId = elderlyId;
        ActorUserId = actorUserId;
        ActivityType = activityType;
        Summary = summary;
        CreatedOnUtc = DateTime.UtcNow;
    }

    public ElderlyId ElderlyId { get; private set; }
    public UserId ActorUserId { get; private set; }
    public ElderlyActivityType ActivityType { get; private set; }
    public string Summary { get; private set; } = default!;
    public DateTime CreatedOnUtc { get; private set; }

    public static ElderlyActivityLog Create(
        ElderlyId elderlyId,
        UserId actorUserId,
        ElderlyActivityType activityType,
        string summary)
    {
        if (string.IsNullOrWhiteSpace(summary))
        {
            throw new DomainException("Activity summary is required.");
        }

        if (summary.Trim().Length > MaximumSummaryLength)
        {
            throw new DomainException($"Activity summary cannot exceed {MaximumSummaryLength} characters.");
        }

        if (!Enum.IsDefined(activityType))
        {
            throw new DomainException("Activity type is invalid.");
        }

        return new ElderlyActivityLog(
            ElderlyActivityLogId.New(),
            elderlyId,
            actorUserId,
            activityType,
            summary.Trim());
    }
}