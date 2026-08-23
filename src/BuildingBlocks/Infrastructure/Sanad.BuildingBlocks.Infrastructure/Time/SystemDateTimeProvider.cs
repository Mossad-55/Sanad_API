using Sanad.BuildingBlocks.Application.Abstractions;

namespace Sanad.BuildingBlocks.Infrastructure.Time;

public sealed class SystemDateTimeProvider :
    IDateTimeProvider
{
    public DateTime UtcNow =>
        DateTime.UtcNow;
}