using Ahlena.BuildingBlocks.Domain.Abstractions;

namespace Ahlena.Modules.Notifications.Domain.Notifications;

public enum DevicePlatform
{
    Android = 1,
    IOS = 2
}

public sealed class DeviceToken : Entity<Guid>
{
    public const int MaximumTokenLength = 2048;
    public const int MaximumAppVersionLength = 64;

    private DeviceToken() { }

    private DeviceToken(Guid id, Guid userId, DevicePlatform platform, string token, string? appVersion, DateTime nowUtc) : base(id)
    {
        UserId = userId;
        Platform = platform;
        Token = token;
        AppVersion = appVersion;
        RegisteredOnUtc = nowUtc;
        LastSeenOnUtc = nowUtc;
    }

    public Guid UserId { get; private set; }
    public DevicePlatform Platform { get; private set; }
    public string Token { get; private set; } = string.Empty;
    public string? AppVersion { get; private set; }
    public DateTime RegisteredOnUtc { get; private set; }
    public DateTime LastSeenOnUtc { get; private set; }

    public static DeviceToken Register(Guid userId, DevicePlatform platform, string token, string? appVersion, DateTime nowUtc)
    {
        if (userId == Guid.Empty) throw new ArgumentException("Device owner is required.", nameof(userId));
        if (!Enum.IsDefined(platform)) throw new ArgumentOutOfRangeException(nameof(platform));
        if (string.IsNullOrWhiteSpace(token) || token.Length > MaximumTokenLength || token.Any(char.IsWhiteSpace))
            throw new ArgumentException("Device token is invalid.", nameof(token));
        if (appVersion?.Length > MaximumAppVersionLength) throw new ArgumentException("App version is too long.", nameof(appVersion));
        if (nowUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Device time must be UTC.", nameof(nowUtc));
        return new DeviceToken(Guid.CreateVersion7(), userId, platform, token, NormalizeVersion(appVersion), nowUtc);
    }

    public void Reassign(Guid userId, DevicePlatform platform, string? appVersion, DateTime nowUtc)
    {
        if (userId == Guid.Empty) throw new ArgumentException("Device owner is required.", nameof(userId));
        UserId = userId;
        Refresh(platform, appVersion, nowUtc);
    }

    public void Refresh(DevicePlatform platform, string? appVersion, DateTime nowUtc)
    {
        if (!Enum.IsDefined(platform)) throw new ArgumentOutOfRangeException(nameof(platform));
        if (appVersion?.Length > MaximumAppVersionLength) throw new ArgumentException("App version is too long.", nameof(appVersion));
        if (nowUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Device time must be UTC.", nameof(nowUtc));
        Platform = platform;
        AppVersion = NormalizeVersion(appVersion);
        LastSeenOnUtc = nowUtc;
    }

    public static string Mask(string token) => token.Length <= 6 ? new string('*', token.Length) : $"…{token[^6..]}";
    private static string? NormalizeVersion(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
