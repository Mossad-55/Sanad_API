namespace Sanad.BuildingBlocks.Domain.Primitives.Ids;

public readonly record struct ElderlyNoteId(Guid Value)
{
    public static ElderlyNoteId New() => new(Guid.CreateVersion7());
    public static ElderlyNoteId Empty => new(Guid.Empty);

    public override string ToString() => Value.ToString();
}