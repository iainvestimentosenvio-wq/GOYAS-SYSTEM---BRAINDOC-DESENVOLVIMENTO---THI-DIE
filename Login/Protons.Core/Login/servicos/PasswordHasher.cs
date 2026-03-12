using System.Security.Cryptography;
using System.Text;
using Protons.Core.Login.Validation;

namespace Protons.Core.Login.Services;

public sealed class PasswordHasher : IPasswordHasher
{
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int DefaultIterations = 100_000;

    public (string Hash, string Salt, int Iterations) HashPassword(string password, int? iterations = null)
    {
        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentNullException(nameof(password));
        if (password.Length > PasswordPolicy.MaxLength)
            throw new ArgumentException($"Password length exceeds limit ({PasswordPolicy.MaxLength}).", nameof(password));
        var iter = iterations ?? DefaultIterations;
        var saltBytes = RandomNumberGenerator.GetBytes(SaltSize);
        var hashBytes = Rfc2898DeriveBytes.Pbkdf2(
            password,
            saltBytes,
            iter,
            HashAlgorithmName.SHA256,
            KeySize);

        return (Convert.ToBase64String(hashBytes), Convert.ToBase64String(saltBytes), iter);
    }

    public bool Verify(string password, string hash, string salt, int iterations)
    {
        if (password is null || hash is null || salt is null ||
            iterations <= 0 ||
            !TryFromBase64String(hash, out var hashBytes) ||
            !TryFromBase64String(salt, out var saltBytes))
        {
            return false;
        }

        var computed = Rfc2898DeriveBytes.Pbkdf2(
            password,
            saltBytes!,
            iterations,
            HashAlgorithmName.SHA256,
            KeySize);

        return CryptographicOperations.FixedTimeEquals(hashBytes!, computed);
    }

    private static bool TryFromBase64String(string value, out byte[]? bytes)
    {
        bytes = null;
        if (string.IsNullOrWhiteSpace(value))
            return false;
        try
        {
            bytes = Convert.FromBase64String(value);
            return bytes.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
