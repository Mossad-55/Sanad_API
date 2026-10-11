using System.Globalization;
using System.Text;

namespace Sanad.Modules.Chat.Application.Messages;

/// <summary>
/// Opaque cursor for endpoint 3's page, following the Notifications
/// <c>NotificationCursor</c> precedent: base64 of <c>{ticks}:{messageId}</c>.
/// The id alone is not enough — the keyset filter is
/// (<c>sentOnUtc</c>, <c>id</c>) — so the cursor carries both. A caller may
/// also pass a raw message id as <c>beforeMessageId</c>; the handler resolves
/// that against the conversation (and answers the privacy 404 when it does
/// not resolve), which is why this type only has to handle its own format.
/// </summary>
internal static class MessageCursor
{
    public static string Encode(DateTime sentOnUtc, Guid id) =>
        Convert.ToBase64String(
            Encoding.UTF8.GetBytes(
                FormattableString.Invariant($"{sentOnUtc.Ticks}:{id:N}")));

    public static bool TryDecode(
        string? value,
        out DateTime sentOnUtc,
        out Guid id)
    {
        sentOnUtc = default;
        id = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            string[] parts = Encoding.UTF8
                .GetString(Convert.FromBase64String(value))
                .Split(':');

            if (parts.Length != 2 ||
                !long.TryParse(
                    parts[0],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out long ticks) ||
                !Guid.TryParseExact(parts[1], "N", out id))
            {
                return false;
            }

            var decoded = new DateTime(ticks, DateTimeKind.Utc);

            if (decoded.Kind != DateTimeKind.Utc)
            {
                return false;
            }

            sentOnUtc = decoded;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
