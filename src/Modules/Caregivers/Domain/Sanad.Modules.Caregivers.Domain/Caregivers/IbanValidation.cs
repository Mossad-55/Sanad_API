using IbanNet;
using IbanNet.Validation.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.Modules.Caregivers.Domain.Caregivers;

/// <summary>
/// IBAN validation against the official SWIFT IBAN Registry as embedded in
/// the pinned IbanNet library version (see central package management).
/// Supported formats are every country registered in that embedded snapshot;
/// no request-time registry fetch is performed. When the pin is bumped, rerun
/// the IBAN vectors and record the new registry snapshot in the payout docs.
/// </summary>
public static class IbanValidation
{
    private static readonly IIbanValidator Validator = new IbanValidator();
    private static readonly IIbanParser Parser = new IbanParser(Validator);

    public static bool TryNormalizeIban(
        string? iban,
        out string normalizedIban,
        out IbanInvalidReason reason)
    {
        normalizedIban = string.Empty;
        reason = IbanInvalidReason.Other;

        if (string.IsNullOrWhiteSpace(iban))
        {
            return false;
        }

        string candidate =
            iban.Replace(" ", string.Empty)
                .Replace("-", string.Empty)
                .ToUpperInvariant();

        ValidationResult result = Validator.Validate(candidate);

        if (!result.IsValid)
        {
            reason = MapReason(result.Error);
            return false;
        }

        if (!Parser.TryParse(candidate, out Iban? parsed) || parsed is null)
        {
            reason = IbanInvalidReason.Other;
            return false;
        }

        normalizedIban = parsed.ToString();
        reason = IbanInvalidReason.None;
        return true;
    }

    public static string NormalizeIbanOrThrow(
        string? iban)
    {
        if (!TryNormalizeIban(iban, out string normalized, out _))
        {
            throw new DomainException(
                "IBAN is invalid.");
        }

        return normalized;
    }

    private static IbanInvalidReason MapReason(
        object? error)
    {
        return error switch
        {
            UnknownCountryCodeResult => IbanInvalidReason.UnknownCountry,
            InvalidLengthResult => IbanInvalidReason.InvalidLength,
            InvalidStructureResult => IbanInvalidReason.InvalidStructure,
            InvalidCheckDigitsResult => IbanInvalidReason.InvalidChecksum,
            IllegalCountryCodeCharactersResult => IbanInvalidReason.IllegalCharacters,
            IllegalCharactersResult => IbanInvalidReason.IllegalCharacters,
            _ => IbanInvalidReason.Other
        };
    }
}

public enum IbanInvalidReason
{
    None = 0,
    UnknownCountry = 1,
    InvalidLength = 2,
    InvalidStructure = 3,
    InvalidChecksum = 4,
    IllegalCharacters = 5,
    Other = 6
}
