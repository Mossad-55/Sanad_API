using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions.Events;

namespace Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;

public sealed class DeviceSession :
    AggregateRoot<DeviceSessionId>
{
    public const int MaximumDeviceNameLength = 200;
    public const int MaximumAppVersionLength = 50;
    public const int MaximumTokenHashLength = 2048;
    public const int MaximumRevocationReasonLength = 1000;

    private DeviceSession()
    {
    }

    private DeviceSession(
        DeviceSessionId id,
        UserId userId,
        string deviceName,
        DevicePlatform platform,
        string appVersion,
        string refreshTokenHash,
        DateTime createdOnUtc,
        DateTime expiresOnUtc)
        : base(id)
    {
        UserId = userId;
        DeviceName = deviceName;
        Platform = platform;
        AppVersion = appVersion;
        RefreshTokenHash = refreshTokenHash;
        CreatedOnUtc = createdOnUtc;
        ExpiresOnUtc = expiresOnUtc;

        RaiseDomainEvent(
            new DeviceSessionCreatedDomainEvent(
                Id,
                UserId));
    }

    public UserId UserId { get; private set; }

    public string DeviceName { get; private set; } =
        string.Empty;

    public DevicePlatform Platform { get; private set; }

    public string AppVersion { get; private set; } =
        string.Empty;

    public string RefreshTokenHash { get; private set; } =
        string.Empty;

    public int RotationCount { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }

    public DateTime? LastRotatedOnUtc { get; private set; }

    public DateTime ExpiresOnUtc { get; private set; }

    public DateTime? RevokedOnUtc { get; private set; }

    public string? RevocationReason { get; private set; }

    public DateTime? ReuseDetectedOnUtc { get; private set; }

    public bool IsRevoked =>
        RevokedOnUtc.HasValue;

    public bool HasReuseDetection =>
        ReuseDetectedOnUtc.HasValue;

    public static DeviceSession Create(
        UserId userId,
        string deviceName,
        DevicePlatform platform,
        string appVersion,
        string refreshTokenHash,
        DateTime createdOnUtc,
        DateTime expiresOnUtc)
    {
        if (userId == UserId.Empty)
        {
            throw new DomainException(
                "User ID is required.");
        }

        if (!Enum.IsDefined(platform) ||
            platform == DevicePlatform.Unknown)
        {
            throw new DomainException(
                "Device platform is invalid.");
        }

        string normalizedDeviceName =
            NormalizeRequiredText(
                deviceName,
                MaximumDeviceNameLength,
                "Device name");

        string normalizedAppVersion =
            NormalizeRequiredText(
                appVersion,
                MaximumAppVersionLength,
                "App version");

        string normalizedTokenHash =
            NormalizeRequiredText(
                refreshTokenHash,
                MaximumTokenHashLength,
                "Refresh token hash");

        ValidateUtc(createdOnUtc);
        ValidateUtc(expiresOnUtc);

        if (expiresOnUtc <= createdOnUtc)
        {
            throw new DomainException(
                "Session expiration must be after creation.");
        }

        return new DeviceSession(
            DeviceSessionId.New(),
            userId,
            normalizedDeviceName,
            platform,
            normalizedAppVersion,
            normalizedTokenHash,
            createdOnUtc,
            expiresOnUtc);
    }

    public void RotateRefreshToken(
        string newRefreshTokenHash,
        DateTime newExpiresOnUtc,
        DateTime utcNow)
    {
        EnsureCanRefresh(utcNow);

        ValidateUtc(newExpiresOnUtc);

        if (newExpiresOnUtc <= utcNow)
        {
            throw new DomainException(
                "New refresh token expiration must be " +
                "after the rotation time.");
        }

        string normalizedHash =
            NormalizeRequiredText(
                newRefreshTokenHash,
                MaximumTokenHashLength,
                "Refresh token hash");

        if (normalizedHash == RefreshTokenHash)
        {
            throw new DomainException(
                "New refresh token must differ from " +
                "the current refresh token.");
        }

        RefreshTokenHash = normalizedHash;
        ExpiresOnUtc = newExpiresOnUtc;
        LastRotatedOnUtc = utcNow;
        RotationCount++;

        RaiseDomainEvent(
            new DeviceSessionRefreshTokenRotatedDomainEvent(
                Id,
                RotationCount,
                ExpiresOnUtc));
    }

    public void RegisterRefreshTokenReuse(
        DateTime utcNow)
    {
        ValidateUtc(utcNow);

        if (ReuseDetectedOnUtc.HasValue)
        {
            return;
        }

        ReuseDetectedOnUtc = utcNow;

        if (!IsRevoked)
        {
            RevokedOnUtc = utcNow;
            RevocationReason =
                "Refresh token reuse detected.";
        }

        RaiseDomainEvent(
            new DeviceSessionRefreshTokenReuseDetectedDomainEvent(
                Id,
                UserId));
    }

    public void Revoke(
        string reason,
        DateTime utcNow)
    {
        ValidateUtc(utcNow);

        if (IsRevoked)
        {
            return;
        }

        string normalizedReason =
            NormalizeRequiredText(
                reason,
                MaximumRevocationReasonLength,
                "Revocation reason");

        RevokedOnUtc = utcNow;
        RevocationReason = normalizedReason;

        RaiseDomainEvent(
            new DeviceSessionRevokedDomainEvent(
                Id,
                UserId,
                normalizedReason));
    }

    public bool IsExpired(
        DateTime utcNow)
    {
        ValidateUtc(utcNow);

        return utcNow >=
            ExpiresOnUtc;
    }

    public bool IsActive(
        DateTime utcNow)
    {
        return !IsRevoked &&
               !IsExpired(utcNow);
    }

    private void EnsureCanRefresh(
        DateTime utcNow)
    {
        ValidateUtc(utcNow);

        if (IsRevoked)
        {
            throw new DomainException(
                "Device session is revoked.");
        }

        if (IsExpired(utcNow))
        {
            throw new DomainException(
                "Device session has expired.");
        }
    }

    private static string NormalizeRequiredText(
        string value,
        int maximumLength,
        string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException(
                $"{fieldName} is required.");
        }

        string normalizedValue =
            value.Trim();

        if (normalizedValue.Length >
            maximumLength)
        {
            throw new DomainException(
                $"{fieldName} cannot exceed " +
                $"{maximumLength} characters.");
        }

        return normalizedValue;
    }

    private static void ValidateUtc(
        DateTime utcNow)
    {
        if (utcNow.Kind !=
            DateTimeKind.Utc)
        {
            throw new DomainException(
                "Session time must be in UTC.");
        }
    }
}