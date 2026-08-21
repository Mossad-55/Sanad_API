using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;

public static class DeviceSessionPolicy
{
    public const int MaximumActiveSessions = 5;

    public static void EnsureCanCreateSession(
        int activeSessionCount)
    {
        if (activeSessionCount < 0)
        {
            throw new DomainException(
                "Active session count cannot be negative.");
        }

        if (activeSessionCount >=
            MaximumActiveSessions)
        {
            throw new DomainException(
                $"A User cannot have more than " +
                $"{MaximumActiveSessions} active sessions.");
        }
    }
}