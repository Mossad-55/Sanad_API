namespace Sanad.Modules.Chat.Domain.Attachments;

public readonly record struct AttachmentId(Guid Value)
{
    public static AttachmentId New() => new(Guid.CreateVersion7());
    public static AttachmentId Empty => new(Guid.Empty);
    public override string ToString() => Value.ToString();
}
