using Ahlena.BuildingBlocks.Application.Abstractions;

namespace Ahlena.BuildingBlocks.Infrastructure.Time;

public sealed class SystemDateTimeProvider :
    IDateTimeProvider
{
    public DateTime UtcNow =>
        DateTime.UtcNow;
}