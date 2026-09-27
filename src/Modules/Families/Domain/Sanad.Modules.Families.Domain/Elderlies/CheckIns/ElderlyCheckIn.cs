using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Families.Domain.Elderlies.CheckIns;

public sealed class ElderlyCheckIn : Entity<Guid>
{
    private ElderlyCheckIn() { }

    private ElderlyCheckIn(Guid id, ElderlyId elderlyId, DateOnly localDate, bool answer, TimeOnly answeredAtLocalTime, DateTime answeredOnUtc, UserId answeredByUserId)
        : base(id)
    {
        ElderlyId = elderlyId;
        LocalDate = localDate;
        Answer = answer;
        AnsweredAtLocalTime = answeredAtLocalTime;
        AnsweredOnUtc = answeredOnUtc;
        AnsweredByUserId = answeredByUserId;
    }

    public ElderlyId ElderlyId { get; private set; }
    public DateOnly LocalDate { get; private set; }
    public bool Answer { get; private set; }
    public TimeOnly AnsweredAtLocalTime { get; private set; }
    public DateTime AnsweredOnUtc { get; private set; }
    public UserId AnsweredByUserId { get; private set; }

    public static ElderlyCheckIn Create(ElderlyId elderlyId, DateOnly localDate, bool answer, TimeOnly answeredAtLocalTime, DateTime answeredOnUtc, UserId answeredByUserId)
    {
        if (elderlyId == ElderlyId.Empty || answeredByUserId == UserId.Empty || answeredOnUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Check-in identifiers and UTC answer time are required.");
        return new ElderlyCheckIn(Guid.NewGuid(), elderlyId, localDate, answer, answeredAtLocalTime, answeredOnUtc, answeredByUserId);
    }
}
