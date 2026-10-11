namespace Sanad.Modules.Chat.Domain.Messages;

/// <summary>
/// Payload kind of a chat message: plain text or a file reference.
/// </summary>
public enum MessageType
{
    Text = 0,
    File = 1
}
