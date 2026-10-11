namespace Sanad.Modules.Chat.Domain.Messages;

/// <summary>
/// Payload kind of a chat message: plain text, a file reference, or a
/// shared location (Opus delta: <c>Location</c> carries
/// latitude/longitude/label with the same participant-only privacy as
/// text).
/// </summary>
public enum MessageType
{
    Text = 0,
    File = 1,
    Location = 2
}
