using System.Text.RegularExpressions;
using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.BuildingBlocks.Domain.ValueObjects;

public sealed class PhoneNumber : ValueObject
{
    private static readonly Regex E164Regex =
        new(
            @"\A\+[1-9][0-9]{1,14}\z",
            RegexOptions.Compiled);

    private PhoneNumber()
    {
    }

    private PhoneNumber(string value)
    {
        Value = value;
    }

    public string Value { get; private set; } = string.Empty;

    public static PhoneNumber Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException("Phone number is required.");
        }

        value = value.Trim();

        if (!E164Regex.IsMatch(value))
        {
            throw new DomainException(
                "Phone number must be in E.164 format.");
        }

        return new PhoneNumber(value);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public static implicit operator string(PhoneNumber phoneNumber)
    {
        return phoneNumber.Value;
    }

    public override string ToString()
    {
        return Value;
    }
}