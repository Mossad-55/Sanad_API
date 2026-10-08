using Microsoft.Extensions.Options;
using Sanad.Modules.Caregivers.Application.Abstractions.Security;
using Sanad.Modules.Caregivers.Infrastructure.Security;

namespace Sanad.UnitTests.Caregivers;

public sealed class AesGcmIbanProtectorTests
{
    private const string Iban = "GB29NWBK60161331926819";
    private const string Key1Id = "k1";
    private const string Key2Id = "k2";

    [Fact]
    public void Protect_ShouldRoundTripWithoutExposingPlaintext()
    {
        IIbanProtector protector = CreateProtector(Key1Id, Key1Id, Key1());

        string envelope = protector.Protect(Iban);

        Assert.StartsWith($"v1.{Key1Id}.", envelope);
        Assert.DoesNotContain(Iban, envelope);
        Assert.Equal(Iban, protector.Unprotect(envelope));
    }

    [Fact]
    public void Protect_ShouldUseRandomNoncePerCall()
    {
        IIbanProtector protector = CreateProtector(Key1Id, Key1Id, Key1());

        Assert.NotEqual(
            protector.Protect(Iban),
            protector.Protect(Iban));
    }

    [Fact]
    public void Unprotect_ShouldFailClosed_WhenPayloadIsTampered()
    {
        IIbanProtector protector = CreateProtector(Key1Id, Key1Id, Key1());

        string envelope = protector.Protect(Iban);
        string tampered = envelope[..^2] + "AA";

        var exception = Assert.Throws<IbanProtectionException>(
            () => protector.Unprotect(tampered));

        Assert.DoesNotContain(Iban, exception.Message);
    }

    [Fact]
    public void Unprotect_ShouldFailClosed_WhenKeyIsUnknown()
    {
        IIbanProtector protector = CreateProtector(Key1Id, Key1Id, Key1());
        string envelope = protector.Protect(Iban);

        IIbanProtector rotated = CreateProtector(Key2Id, Key2Id, Key2());

        Assert.Throws<IbanProtectionException>(
            () => rotated.Unprotect(envelope));
    }

    [Fact]
    public void Rotation_ShouldKeepOldCiphertextReadable()
    {
        IIbanProtector before = CreateProtector(Key1Id, Key1Id, Key1());
        string oldEnvelope = before.Protect(Iban);

        var options = new PayoutIbanProtectionOptions
        {
            CurrentKeyId = Key2Id,
            Keys = new Dictionary<string, string>
            {
                [Key1Id] = Key1(),
                [Key2Id] = Key2()
            }
        };

        IIbanProtector after =
            new AesGcmIbanProtector(new FixedOptionsMonitor(options));

        Assert.Equal(Iban, after.Unprotect(oldEnvelope));

        string newEnvelope = after.Protect(Iban);
        Assert.StartsWith($"v1.{Key2Id}.", newEnvelope);
        Assert.Equal(Iban, after.Unprotect(newEnvelope));
    }

    [Fact]
    public void Protect_ShouldFailClosed_WhenUnconfigured()
    {
        IIbanProtector protector =
            new AesGcmIbanProtector(
                new FixedOptionsMonitor(new PayoutIbanProtectionOptions()));

        var exception = Assert.Throws<IbanProtectionException>(
            () => protector.Protect(Iban));

        Assert.DoesNotContain(Iban, exception.Message);
    }

    [Fact]
    public void Unprotect_ShouldFailClosed_WhenUnconfigured()
    {
        IIbanProtector protector =
            new AesGcmIbanProtector(
                new FixedOptionsMonitor(new PayoutIbanProtectionOptions()));

        Assert.Throws<IbanProtectionException>(
            () => protector.Unprotect("v1.k1.payload"));
    }

    [Fact]
    public void Protect_ShouldFailClosed_WhenKeyIsInvalid()
    {
        var options = new PayoutIbanProtectionOptions
        {
            CurrentKeyId = Key1Id,
            Keys = new Dictionary<string, string>
            {
                [Key1Id] = "not-a-key"
            }
        };

        IIbanProtector protector =
            new AesGcmIbanProtector(new FixedOptionsMonitor(options));

        Assert.Throws<IbanProtectionException>(
            () => protector.Protect(Iban));
    }

    private static IIbanProtector CreateProtector(
        string currentKeyId,
        string keyId,
        string key)
    {
        return new AesGcmIbanProtector(
            new FixedOptionsMonitor(
                new PayoutIbanProtectionOptions
                {
                    CurrentKeyId = currentKeyId,
                    Keys = new Dictionary<string, string>
                    {
                        [keyId] = key
                    }
                }));
    }

    private static string Key1() =>
        Convert.ToBase64String(
            Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

    private static string Key2() =>
        Convert.ToBase64String(
            Enumerable.Range(33, 32).Select(i => (byte)i).ToArray());

    private sealed class FixedOptionsMonitor(
        PayoutIbanProtectionOptions value) : IOptionsMonitor<PayoutIbanProtectionOptions>
    {
        public PayoutIbanProtectionOptions CurrentValue => value;

        public PayoutIbanProtectionOptions Get(
            string? name) => value;

        public IDisposable OnChange(
            Action<PayoutIbanProtectionOptions, string?> listener) =>
            new NoopDisposable();

        private sealed class NoopDisposable : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
