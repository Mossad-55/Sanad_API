using Sanad.BuildingBlocks.Application.Idempotency;

namespace Sanad.UnitTests.BuildingBlocks;

/// <summary>
/// Canonical fingerprint contract: deterministic, order-sensitive, lowercase
/// SHA-256 hex, with a null field distinct from an empty one and every
/// server-derived value excluded by the caller (never by this helper).
/// </summary>
public sealed class IdempotencyFingerprintTests
{
    [Fact]
    public void Compute_IsDeterministic()
    {
        var fields = new (string, string?)[]
        {
            ("elderlyId", "8a1c2f3e-0000-0000-0000-000000000001"),
            ("bookingDate", "2026-11-01"),
            ("serviceAddress", "12 Nile St")
        };

        string first = IdempotencyFingerprint.Compute("family.bookings.checkout", fields);
        string second = IdempotencyFingerprint.Compute("family.bookings.checkout", fields);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Compute_ReturnsSixtyFourLowercaseHexCharacters()
    {
        string fingerprint = IdempotencyFingerprint.Compute(
            "family.care-records.create",
            ("details", "d"));

        Assert.Equal(64, fingerprint.Length);
        Assert.All(
            fingerprint,
            character => Assert.True(
                character is >= '0' and <= '9' or >= 'a' and <= 'f',
                $"Unexpected fingerprint character '{character}'."));
    }

    [Fact]
    public void Compute_IsOrderSensitive()
    {
        string ordered = IdempotencyFingerprint.Compute(
            "op",
            ("a", "1"),
            ("b", "2"));
        string reversed = IdempotencyFingerprint.Compute(
            "op",
            ("b", "2"),
            ("a", "1"));

        Assert.NotEqual(ordered, reversed);
    }

    [Fact]
    public void Compute_IncludesTheFieldNameAndOperation()
    {
        string baseline = IdempotencyFingerprint.Compute("op", ("a", "1"));
        string renamedField = IdempotencyFingerprint.Compute("op", ("b", "1"));
        string renamedOperation = IdempotencyFingerprint.Compute("other-op", ("a", "1"));

        Assert.NotEqual(baseline, renamedField);
        Assert.NotEqual(baseline, renamedOperation);
    }

    [Fact]
    public void Compute_DistinguishesNullFromEmpty()
    {
        string nullValue = IdempotencyFingerprint.Compute(
            "op",
            ("specialInstructions", null));
        string emptyValue = IdempotencyFingerprint.Compute(
            "op",
            ("specialInstructions", string.Empty));
        string literalNullText = IdempotencyFingerprint.Compute(
            "op",
            ("specialInstructions", "<null>"));

        Assert.NotEqual(nullValue, emptyValue);
        // The documented serialization of null is the literal <null>.
        Assert.Equal(nullValue, literalNullText);
    }

    [Fact]
    public void Compute_ReturnsAnErrorForAMissingOperation()
    {
        Assert.Throws<ArgumentException>(
            () => IdempotencyFingerprint.Compute(" ", ("a", "1")));
    }

    [Fact]
    public void Helpers_NormalizeRouteAndBodyValues()
    {
        Guid id = Guid.Parse("8A1C2F3E-0000-0000-0000-000000000001");

        Assert.Equal("8a1c2f3e-0000-0000-0000-000000000001", IdempotencyFingerprint.Id(id));
        Assert.Equal("8a1c2f3e-0000-0000-0000-000000000001", IdempotencyFingerprint.Id((Guid?)id));
        Assert.Null(IdempotencyFingerprint.Id((Guid?)null));
        Assert.Equal("2026-11-01", IdempotencyFingerprint.Date(new DateOnly(2026, 11, 1)));
        Assert.Equal("08:05", IdempotencyFingerprint.Time(new TimeOnly(8, 5)));
        Assert.Equal("HomeVisit", IdempotencyFingerprint.Enum(Sanad.BuildingBlocks.Domain.Enums.BookingShiftType.HomeVisit));
        Assert.Equal("123.45", IdempotencyFingerprint.Decimal(123.45m));
        Assert.Equal("trimmed", IdempotencyFingerprint.Text("  trimmed "));
        Assert.Null(IdempotencyFingerprint.Text(null));
    }
}
