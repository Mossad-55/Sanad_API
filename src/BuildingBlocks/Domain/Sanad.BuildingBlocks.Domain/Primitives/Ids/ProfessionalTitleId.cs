namespace Sanad.BuildingBlocks.Domain.Primitives.Ids;

public readonly record struct ProfessionalTitleId(Guid Value)
{
    public static ProfessionalTitleId New() => new(Guid.CreateVersion7());

    public static ProfessionalTitleId Empty => new(Guid.Empty);

    public override string ToString() => Value.ToString();
}