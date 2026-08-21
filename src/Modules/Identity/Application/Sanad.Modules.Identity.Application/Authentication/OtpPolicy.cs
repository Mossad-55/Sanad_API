namespace Sanad.Modules.Identity.Application.Authentication;

public static class OtpPolicy
{
    public const int CodeLength = 6;
    public const int MaximumAttempts = 5;

    public static readonly TimeSpan Lifetime =
        TimeSpan.FromMinutes(5);

    public static readonly TimeSpan ResendCooldown =
        TimeSpan.FromSeconds(60);
}