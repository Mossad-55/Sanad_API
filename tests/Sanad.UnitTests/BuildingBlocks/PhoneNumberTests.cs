using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.ValueObjects;

namespace Sanad.UnitTests.BuildingBlocks;

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