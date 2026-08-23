using System.Security.Cryptography;
using System.Text;
using Sanad.Modules.Identity.Application.Abstractions.Security;

namespace Sanad.Modules.Identity.Infrastructure.Security;

public sealed class Pbkdf2OtpService :
    IOtpService
{
    private const int SaltSize = 16;
    private const int DerivedKeySize = 32;
    private const int Iterations = 100_000;
    private const string Version = "v1";

    public GeneratedOtpCode Generate(
        int length)
    {
        if (length != 6)
        {
            throw new ArgumentOutOfRangeException(
                nameof(length),
                "OTP length must be six.");
        }

        Span<char> codeCharacters =
            stackalloc char[length];

        for (int index = 0;
             index < length;
             index++)
        {
            int digit =
                RandomNumberGenerator.GetInt32(10);

            codeCharacters[index] =
                (char)('0' + digit);
        }

        string plainTextCode =
            new(codeCharacters);

        return new GeneratedOtpCode(
            plainTextCode,
            Hash(plainTextCode));
    }

    public bool Verify(
        string providedCode,
        string otpHash)
    {
        if (!IsSixAsciiDigits(
                providedCode) ||
            string.IsNullOrWhiteSpace(
                otpHash))
        {
            return false;
        }

        string[] parts =
            otpHash.Split(
                ':',
                StringSplitOptions.None);

        if (parts.Length != 4 ||
            parts[0] != Version ||
            !int.TryParse(
                parts[1],
                out int iterations) ||
            iterations <= 0)
        {
            return false;
        }

        try
        {
            byte[] salt =
                Convert.FromBase64String(
                    parts[2]);

            byte[] expectedKey =
                Convert.FromBase64String(
                    parts[3]);

            if (salt.Length != SaltSize ||
                expectedKey.Length !=
                    DerivedKeySize)
            {
                return false;
            }

            byte[] actualKey =
                Rfc2898DeriveBytes.Pbkdf2(
                    providedCode,
                    salt,
                    iterations,
                    HashAlgorithmName.SHA256,
                    expectedKey.Length);

            return CryptographicOperations
                .FixedTimeEquals(
                    actualKey,
                    expectedKey);
        }
        catch (
            FormatException)
        {
            return false;
        }
        catch (
            CryptographicException)
        {
            return false;
        }
    }

    private static string Hash(
        string code)
    {
        byte[] salt =
            RandomNumberGenerator.GetBytes(
                SaltSize);

        byte[] derivedKey =
            Rfc2898DeriveBytes.Pbkdf2(
                code,
                salt,
                Iterations,
                HashAlgorithmName.SHA256,
                DerivedKeySize);

        return string.Join(
            ':',
            Version,
            Iterations,
            Convert.ToBase64String(salt),
            Convert.ToBase64String(
                derivedKey));
    }

    private static bool IsSixAsciiDigits(
        string value)
    {
        return value.Length == 6 &&
               value.All(
                   character =>
                       character is >= '0' and <= '9');
    }
}