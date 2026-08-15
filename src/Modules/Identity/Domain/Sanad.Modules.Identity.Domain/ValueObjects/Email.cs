using System.Text.RegularExpressions;
using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.Modules.Identity.Domain.ValueObjects;

public sealed class Email : ValueObject
{
    private static readonly Regex EmailRegex =
        new(
            @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
            RegexOptions.Compiled);

    private Email()
    {
    }

    private Email(string value)
    {
        Value = value;
    }

    public string Value { get; private set; } = string.Empty;

    public static Email Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException("Email is required.");
        }

        value = value.Trim().ToLowerInvariant();

        if (value.Length > 256)
        {
            throw new DomainException("Email is too long.");
        }

        if (!EmailRegex.IsMatch(value))
        {
            throw new DomainException("Invalid email address.");
        }

        return new Email(value);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public static implicit operator string(Email email)
    {
        return email.Value;
    }

    public override string ToString()
    {
        return Value;
    }
}