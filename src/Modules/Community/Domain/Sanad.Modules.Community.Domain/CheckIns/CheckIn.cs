using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Community.Domain.Posts;

namespace Sanad.Modules.Community.Domain.CheckIns;

public sealed class CheckIn : Entity<CommunityCheckInId>
{
    private CheckIn()
    {
    }

    private CheckIn(
        CommunityCheckInId id,
        CommunityPostId postId,
        UserId userId,
        bool answer,
        string timeZoneId,
        DateOnly localDate,
        TimeOnly answeredAtLocalTime,
        DateTime answeredOnUtc)
        : base(id)
    {
        if (postId == CommunityPostId.Empty)
            throw new DomainException("A Community check-in requires a post.");
        if (userId == UserId.Empty)
            throw new DomainException("A Community check-in requires an author.");
        PostId = postId;
        UserId = userId;
        UpdateAnswer(answer, timeZoneId, localDate, answeredAtLocalTime, answeredOnUtc);
    }

    public CommunityPostId PostId { get; private set; }
    public UserId UserId { get; private set; }
    public bool Answer { get; private set; }
    public string TimeZoneId { get; private set; } = string.Empty;
    public DateOnly LocalDate { get; private set; }
    public TimeOnly AnsweredAtLocalTime { get; private set; }
    public DateTime AnsweredOnUtc { get; private set; }
    public Post Post { get; private set; } = null!;

    public static CheckIn Create(
        CommunityPostId postId,
        UserId userId,
        bool answer,
        string timeZoneId,
        DateOnly localDate,
        TimeOnly answeredAtLocalTime,
        DateTime answeredOnUtc) =>
        new(CommunityCheckInId.New(), postId, userId, answer, timeZoneId,
            localDate, answeredAtLocalTime, answeredOnUtc);

    public void UpdateAnswer(
        bool answer,
        string timeZoneId,
        DateOnly localDate,
        TimeOnly answeredAtLocalTime,
        DateTime answeredOnUtc)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId) || timeZoneId.Trim().Length > 100)
            throw new DomainException("A valid time zone is required for a Community check-in.");
        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException exception)
        {
            throw new DomainException($"Unknown time zone: {exception.Message}");
        }
        catch (InvalidTimeZoneException exception)
        {
            throw new DomainException($"Invalid time zone: {exception.Message}");
        }
        if (answeredOnUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("Community check-in timestamps must be UTC.");

        Answer = answer;
        TimeZoneId = timeZoneId.Trim();
        LocalDate = localDate;
        AnsweredAtLocalTime = answeredAtLocalTime;
        AnsweredOnUtc = answeredOnUtc;
    }
}
