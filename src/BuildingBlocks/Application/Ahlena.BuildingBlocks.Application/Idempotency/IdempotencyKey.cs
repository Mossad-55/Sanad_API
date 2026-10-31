using Ahlena.BuildingBlocks.Application.Results;

namespace Ahlena.BuildingBlocks.Application.Idempotency;

/// <summary>
/// A normalized optional <c>Idempotency-Key</c> header value.
/// <para>
/// The header is optional: an absent header (or one that is only whitespace)
/// keeps today's byte-identical behavior and produces a null key. A present
/// header is trimmed and must then be 1–128 printable ASCII characters
/// (<c>0x20..0x7E</c>). Parsing happens exactly once, in the controller, so a
/// rejected key never reaches a handler and never writes an idempotency row.
/// </para>
/// </summary>
public sealed record IdempotencyKey
{
    public const int MaximumLength = 128;

    /// <summary>
    /// Stable code the shared kit reports for a malformed key. Each route
    /// surfaces its own owning vocabulary code on the wire; this value is the
    /// neutral kit-level reason carried by <see cref="TryParse"/>.
    /// </summary>
    public const string InvalidCode = "IdempotencyKey.Invalid";

    public IdempotencyKey(string value)
    {
        if (!IsValid(value))
        {
            throw new ArgumentException(
                "An idempotency key must be 1-128 printable ASCII characters.",
                nameof(value));
        }

        Value = value;
    }

    /// <summary>The trimmed, validated key text.</summary>
    public string Value { get; }

    /// <summary>
    /// Parses the raw header value once. Returns <c>true</c> when the outcome
    /// is usable: absent/whitespace yields a null key and a null error; a valid
    /// value yields the key; a malformed value yields a null key and an error.
    /// Returns <c>false</c> only for a malformed value, so controllers can
    /// answer their coded 400 without inspecting the error text.
    /// </summary>
    public static bool TryParse(
        string? raw,
        out IdempotencyKey? key,
        out Error? error)
    {
        key = null;
        error = null;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        string trimmed = raw.Trim();

        if (trimmed.Length is 0 or > MaximumLength)
        {
            error = InvalidKeyError();
            return false;
        }

        foreach (char character in trimmed)
        {
            if (character is < '\u0020' or > '\u007E')
            {
                error = InvalidKeyError();
                return false;
            }
        }

        key = new IdempotencyKey(trimmed);
        return true;
    }

    private static bool IsValid(string? value) =>
        value is { Length: > 0 and <= MaximumLength } &&
        value.All(character => character is >= '\u0020' and <= '\u007E');

    private static Error InvalidKeyError() =>
        new(
            InvalidCode,
            "The Idempotency-Key header must be 1-128 printable ASCII characters.");
}
