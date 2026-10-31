namespace Ahlena.Modules.Chat.Application.RateLimiting;

/// <summary>
/// Configurable Chat Plus abuse-control limits (section `ChatPlus:RateLimits`).
/// Defaults are the contracts.md proposed values. Windows are sliding (no
/// reset burst at clock-minute boundaries); `Retry-After` is the earliest
/// token/window release.
/// </summary>
public sealed record ChatRateOptions
{
    public int SendsPerMinutePerUser { get; init; } = 60;

    public int SendsPerMinutePerConversation { get; init; } = 30;

    public int OpensPerMinutePerUser { get; init; } = 30;

    public int UploadsPerMinutePerUser { get; init; } = 10;

    public int UploadConcurrentPerUser { get; init; } = 2;

    public int ReportsPerHourPerUser { get; init; } = 5;

    public int SyncPerMinutePerConversation { get; init; } = 60;

    public int SyncPerMinutePerUser { get; init; } = 300;

    public int NegotiatePerMinutePerUser { get; init; } = 30;

    public long UnboundQuotaBytesPerUser { get; init; } = 104_857_600;

    public TimeSpan UnboundAttachmentLifetime { get; init; } = TimeSpan.FromHours(24);
}
