using Ahlena.BuildingBlocks.Application.Idempotency;

namespace Ahlena.UnitTests.BuildingBlocks;

/// <summary>
/// Header normalization contract for the four approved creates: absent and
/// whitespace-only headers stay keyless (byte-identical behavior), a present
/// key is trimmed and must be 1-128 printable ASCII.
/// </summary>
public sealed class IdempotencyKeyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public void TryParse_AbsentOrWhitespace_YieldsNoKeyAndNoError(string? raw)
    {
        bool accepted = IdempotencyKey.TryParse(raw, out IdempotencyKey? key, out var error);

        Assert.True(accepted);
        Assert.Null(key);
        Assert.Null(error);
    }

    [Fact]
    public void TryParse_TrimsThePresentKey()
    {
        bool accepted = IdempotencyKey.TryParse("  checkout-0001 \t", out IdempotencyKey? key, out var error);

        Assert.True(accepted);
        Assert.NotNull(key);
        Assert.Equal("checkout-0001", key!.Value);
        Assert.Null(error);
    }

    [Fact]
    public void TryParse_AcceptsTheLengthBoundaries()
    {
        string minimum = "k";
        string maximum = new('a', IdempotencyKey.MaximumLength);

        Assert.True(IdempotencyKey.TryParse(minimum, out IdempotencyKey? shortKey, out _));
        Assert.True(IdempotencyKey.TryParse(maximum, out IdempotencyKey? longKey, out _));

        Assert.Equal(minimum, shortKey!.Value);
        Assert.Equal(maximum, longKey!.Value);
    }

    [Fact]
    public void TryParse_RejectsAnOverlongKey()
    {
        string overlong = new('a', IdempotencyKey.MaximumLength + 1);

        Assert.False(IdempotencyKey.TryParse(overlong, out IdempotencyKey? key, out var error));
        Assert.Null(key);
        Assert.NotNull(error);
        Assert.Equal(IdempotencyKey.InvalidCode, error!.Code);
    }

    [Theory]
    [InlineData("key with\ttab")]
    [InlineData("key with\nnewline")]
    [InlineData("clé")]
    [InlineData("key\u007F")]
    public void TryParse_RejectsNonPrintableAscii(string raw)
    {
        Assert.False(IdempotencyKey.TryParse(raw, out IdempotencyKey? key, out var error));
        Assert.Null(key);
        Assert.Equal(IdempotencyKey.InvalidCode, error!.Code);
    }

    [Fact]
    public void Constructor_RejectsAnInvalidValue()
    {
        Assert.Throws<ArgumentException>(() => new IdempotencyKey(""));
        Assert.Throws<ArgumentException>(() => new IdempotencyKey("has\ttab"));
        Assert.Throws<ArgumentException>(
            () => new IdempotencyKey(new string('a', IdempotencyKey.MaximumLength + 1)));
    }

    [Fact]
    public void Constructor_KeepsAValidValueVerbatim()
    {
        var key = new IdempotencyKey("Order-123_abc.def");

        Assert.Equal("Order-123_abc.def", key.Value);
    }
}
