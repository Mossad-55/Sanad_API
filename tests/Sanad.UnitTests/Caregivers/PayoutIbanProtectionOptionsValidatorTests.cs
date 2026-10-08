using Microsoft.Extensions.Options;
using Sanad.Modules.Caregivers.Infrastructure.Security;

namespace Sanad.UnitTests.Caregivers;

public sealed class PayoutIbanProtectionOptionsValidatorTests
{
    private readonly PayoutIbanProtectionOptionsValidator _validator = new();

    [Fact]
    public void Validate_ShouldSucceed_WhenUnconfigured()
    {
        ValidateOptionsResult result = _validator.Validate(
            null,
            new PayoutIbanProtectionOptions());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_ShouldSucceed_WhenKeysAreValid()
    {
        ValidateOptionsResult result = _validator.Validate(
            null,
            new PayoutIbanProtectionOptions
            {
                CurrentKeyId = "k1",
                Keys = new Dictionary<string, string>
                {
                    ["k1"] = ValidKey(),
                    ["k0"] = ValidKey()
                }
            });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_ShouldFail_WhenCurrentKeyIsMissing()
    {
        ValidateOptionsResult result = _validator.Validate(
            null,
            new PayoutIbanProtectionOptions
            {
                CurrentKeyId = "k2",
                Keys = new Dictionary<string, string>
                {
                    ["k1"] = ValidKey()
                }
            });

        Assert.True(result.Failed);
    }

    [Fact]
    public void Validate_ShouldFail_WhenKeyIsNot256Bit()
    {
        ValidateOptionsResult result = _validator.Validate(
            null,
            new PayoutIbanProtectionOptions
            {
                CurrentKeyId = "k1",
                Keys = new Dictionary<string, string>
                {
                    ["k1"] = Convert.ToBase64String(new byte[16])
                }
            });

        Assert.True(result.Failed);
    }

    private static string ValidKey() =>
        Convert.ToBase64String(new byte[32]);
}
