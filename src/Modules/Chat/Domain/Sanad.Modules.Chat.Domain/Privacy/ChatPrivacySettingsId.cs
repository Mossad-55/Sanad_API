namespace Sanad.Modules.Chat.Domain.Privacy;

public readonly record struct ChatPrivacySettingsId(Guid Value)
{
    public static ChatPrivacySettingsId New() => new(Guid.CreateVersion7());
    public static ChatPrivacySettingsId Empty => new(Guid.Empty);
    public override string ToString() => Value.ToString();
}
