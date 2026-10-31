using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Ahlena.Modules.Chat.Application.Sync;

namespace Ahlena.API.ChatIntegration;

/// <summary>
/// DataProtection-backed sync cursor protector (purpose-scoped, versioned
/// payload). Cursors are opaque, authenticated, and bound to the
/// conversation + requester + mode; tampered, foreign, or cross-user tokens
/// fail closed as privacy 404s at the handler. No cursor content is ever
/// client-constructible.
/// </summary>
public sealed class DataProtectionChatSyncCursorProtector : IChatSyncCursorProtector
{
    private readonly IDataProtector _protector;

    public DataProtectionChatSyncCursorProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("Ahlena.Chat.SyncCursor.v1");
    }

    public string Protect(ChatSyncCursor cursor)
    {
        string json = JsonSerializer.Serialize(cursor);
        return _protector.Protect(json);
    }

    public bool TryUnprotect(string? token, out ChatSyncCursor? cursor)
    {
        cursor = null;

        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        try
        {
            string json = _protector.Unprotect(token);
            ChatSyncCursor? parsed = JsonSerializer.Deserialize<ChatSyncCursor>(json);

            if (parsed is null ||
                parsed.ConversationId == Guid.Empty ||
                parsed.RequesterId == Guid.Empty)
            {
                return false;
            }

            cursor = parsed;
            return true;
        }
        catch (Exception ex) when (
            ex is FormatException ||
            ex is JsonException ||
            ex is System.Security.Cryptography.CryptographicException)
        {
            return false;
        }
    }
}
