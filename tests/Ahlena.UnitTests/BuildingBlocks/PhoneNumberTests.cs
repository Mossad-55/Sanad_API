using Ahlena.BuildingBlocks.Domain.Exceptions;
using Ahlena.BuildingBlocks.Domain.ValueObjects;

namespace Ahlena.UnitTests.BuildingBlocks;

public sealed class PhoneNumberTests
{
    [Theory]
    [InlineData("+2٠١٠٠١٢٣٤٥٦٧")]
    public void Create_ShouldRejectNonAsciiOrNonExactE164(
        string value)
    {
        Assert.Throws<DomainException>(
            () => PhoneNumber.Create(
                value));
    }
}