using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions.Events;

namespace Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;

public sealed class DeviceSession : AggregateRoot<DeviceSessionId>
{
    private DeviceSession(
        DeviceSessionId id,
        UserId userId,
        string deviceName,
        DevicePlatform platform,
        string appVersion,
        string refreshTokenHash,
        DateTime expiresOnUtc
    )
        : base(id)
    {
        UserId = userId;
        DeviceName = deviceName;
        Platform = platform;
        AppVersion = appVersion;
        RefreshTokenHash = refreshTokenHash;

        CreatedOnUtc = DateTime.UtcNow;
        ExpiresOnUtc = expiresOnUtc;

        RaiseDomainEvent(new DeviceSessionCreatedDomainEvent(Id));
    }

    private DeviceSession()
    {
    }

    public UserId UserId { get; private set; }

    public string DeviceName { get; private set; } = string.Empty;

    public DevicePlatform Platform { get; private set; }

    public string AppVersion { get; private set; } = string.Empty;

    public string RefreshTokenHash { get; private set; } = string.Empty;

    public DateTime CreatedOnUtc { get; private set; }

    public DateTime ExpiresOnUtc { get; private set; }

    public DateTime? RevokedOnUtc { get; private set; }

    public bool IsRevoked => RevokedOnUtc.HasValue;

    public static DeviceSession Create(
        UserId userId,
        string deviceName,
        DevicePlatform platform,
        string appVersion,
        string refreshTokenHash,
        DateTime expiresOnUtc)
    {
        if (userId == UserId.Empty)
        {
            throw new DomainException(
                "UserId cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(deviceName))
        {
            throw new DomainException(
                "Device name cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(refreshTokenHash))
        {
            throw new DomainException(
                "Refresh token hash cannot be empty.");
        }



        return new DeviceSession(
            DeviceSessionId.New(),
            userId,
            deviceName.Trim(),
            platform,
            appVersion.Trim(),
            refreshTokenHash,
            expiresOnUtc);
    }

    public void Revoke()
    {
        if (IsRevoked)
        {
            return;
        }

        RevokedOnUtc = DateTime.UtcNow;

        RaiseDomainEvent(new DeviceSessionRevokedDomainEvent(Id));
    }

    public bool IsExpired()
    {
        return DateTime.UtcNow >= ExpiresOnUtc;
    }
}