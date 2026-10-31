namespace Ahlena.Modules.Identity.Domain.Support;

public readonly record struct PartnerContactId(Guid Value)
{
    public static PartnerContactId New() =>
        new(Guid.CreateVersion7());

    public static PartnerContactId Empty =>
        new(Guid.Empty);

    public override string ToString() =>
        Value.ToString();
}
