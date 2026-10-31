namespace Ahlena.API.Seeding;

internal static class PaymobSecretKeySafety
{
    public static bool IsLive(string? secretKey) =>
        secretKey?.StartsWith("sk_live", StringComparison.OrdinalIgnoreCase) == true
        || secretKey?.StartsWith("egy_sk_live_", StringComparison.OrdinalIgnoreCase) == true;
}
