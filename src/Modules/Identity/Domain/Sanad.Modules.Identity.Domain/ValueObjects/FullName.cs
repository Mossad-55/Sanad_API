using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.Modules.Identity.Domain.ValueObjects;

public sealed class FullName : ValueObject
{
    private FullName()
    {
    }

    private FullName(string value)
    {
        Value = value;
    }

    public string Value { get; private set; } = string.Empty;

    public static FullName Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException("Full name is required.");
        }

        value = value.Trim();

        if (value.Length < 2)
        {
            throw new DomainException("Full name is too short.");
        }

        if (value.Length > 200)
        {
            throw new DomainException("Full name is too long.");
        }

        return new FullName(value);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public static implicit operator string(FullName fullName)
    {
        return fullName.Value;
    }

    public override string ToString()
    {
        return Value;
    }
}