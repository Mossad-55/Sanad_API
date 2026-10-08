using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Sanad.Modules.Caregivers.Application.Abstractions.Security;

namespace Sanad.Modules.Caregivers.Infrastructure.Security;

/// <summary>
/// Purpose-scoped AES-256-GCM protector for caregiver payout IBANs.
/// Envelopes carry their key id (`v1.{keyId}.{base64}`) so rotated keys keep
/// decrypting existing ciphertext while new writes use the current key.
/// Keys come only from configuration (environment), never from the database,
/// the repository, or code. Missing or invalid configuration fails closed.
/// </summary>
public sealed class AesGcmIbanProtector : IIbanProtector
{
    private const string EnvelopeVersion = "v1";
    private const int NonceSize = 12;
    private const int KeySize = 32;

    private readonly IOptionsMonitor<PayoutIbanProtectionOptions> _options;

    public AesGcmIbanProtector(
        IOptionsMonitor<PayoutIbanProtectionOptions> options)
    {
        _options = options;
    }

    public string Protect(
        string plaintextIban)
    {
        PayoutIbanProtectionOptions options = CurrentOptions();
        byte[] key = ResolveKey(options, options.CurrentKeyId);

        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] plaintext = Encoding.UTF8.GetBytes(plaintextIban);
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[AesGcm.TagByteSizes.MaxSize];

        using var aes = new AesGcm(key, AesGcm.TagByteSizes.MaxSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        byte[] payload = new byte[nonce.Length + ciphertext.Length + tag.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, nonce.Length);
        Buffer.BlockCopy(ciphertext, 0, payload, nonce.Length, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, payload, nonce.Length + ciphertext.Length, tag.Length);

        CryptographicOperations.ZeroMemory(key);
        CryptographicOperations.ZeroMemory(plaintext);

        return string.Concat(
            EnvelopeVersion,
            ".",
            options.CurrentKeyId,
            ".",
            Convert.ToBase64String(payload));
    }

    public string Unprotect(
        string envelope)
    {
        PayoutIbanProtectionOptions options = CurrentOptions();

        string[] parts = (envelope ?? string.Empty).Split('.');

        if (parts.Length != 3 || parts[0] != EnvelopeVersion)
        {
            throw new IbanProtectionException();
        }

        byte[] key = ResolveKey(options, parts[1]);

        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(parts[2]);
        }
        catch (FormatException)
        {
            throw new IbanProtectionException();
        }

        if (payload.Length < NonceSize + AesGcm.TagByteSizes.MaxSize + 1)
        {
            throw new IbanProtectionException();
        }

        byte[] nonce = payload[..NonceSize];
        byte[] tag = payload[^AesGcm.TagByteSizes.MaxSize..];
        byte[] ciphertext = payload[NonceSize..^AesGcm.TagByteSizes.MaxSize];
        byte[] plaintext = new byte[ciphertext.Length];

        try
        {
            using var aes = new AesGcm(key, AesGcm.TagByteSizes.MaxSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
        }
        catch (CryptographicException)
        {
            throw new IbanProtectionException();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }

        string result = Encoding.UTF8.GetString(plaintext);
        CryptographicOperations.ZeroMemory(plaintext);
        return result;
    }

    private PayoutIbanProtectionOptions CurrentOptions()
    {
        PayoutIbanProtectionOptions options = _options.CurrentValue;

        if (!options.IsConfigured)
        {
            throw new IbanProtectionException();
        }

        return options;
    }

    private static byte[] ResolveKey(
        PayoutIbanProtectionOptions options,
        string keyId)
    {
        if (string.IsNullOrWhiteSpace(keyId) ||
            !options.Keys.TryGetValue(keyId, out string? encoded))
        {
            throw new IbanProtectionException();
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(encoded);
        }
        catch (FormatException)
        {
            throw new IbanProtectionException();
        }

        if (key.Length != KeySize)
        {
            CryptographicOperations.ZeroMemory(key);
            throw new IbanProtectionException();
        }

        return key;
    }
}
