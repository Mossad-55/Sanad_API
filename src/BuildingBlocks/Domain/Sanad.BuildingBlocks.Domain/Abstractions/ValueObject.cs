namespace Sanad.BuildingBlocks.Domain.Abstractions;

public abstract class ValueObject
{
    protected abstract IEnumerable<object?> GetEqualityComponents();

    public override bool Equals(object? obj)
    {
        if(obj is not ValueObject other)
            return false;

        return GetEqualityComponents()
            .SequenceEqual(other.GetEqualityComponents());
    }

    public override int GetHashCode()
    {
        return GetEqualityComponents()
            .Aggregate(
                0,
                (current, obj) =>
                    HashCode.Combine(current, obj));

    }

    public static bool operator ==(
        ValueObject? left,
        ValueObject? right)
    {
        return Equals(left, right);
    }

    public static bool operator !=(
        ValueObject? left,
        ValueObject? right)
    {
        return !Equals(left, right);
    }
}