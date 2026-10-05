namespace Sanad.Modules.Community.Application.CheckIns;

public sealed record AddCheckInCommand : ICommand<bool>
{
    public Guid PostId { get; init; }
    public Guid UserId { get; init; }
    public bool Answer { get; init; }
    public string TimeZoneId { get; init; } = string.Empty;
    public DateOnly LocalDate { get; init; }
    public TimeOnly AnsweredAtLocalTime { get; init; }
}
