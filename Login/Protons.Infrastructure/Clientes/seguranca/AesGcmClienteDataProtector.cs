using System.Security.Cryptography;
using System.Text;
using Protons.Core.Clientes.Security;

namespace Protons.Infrastructure.Clientes.Security;

public sealed class AesGcmClienteDataProtector : IClienteDataProtector
{
    private const string VersionPrefix = "v1";
    private readonly byte[] _key;
    private readonly byte[] _hmacKey;

    public AesGcmClienteDataProtector(byte[] key)
    {
        if (key is null || key.Length != 32)
            throw new ArgumentException("A chave BYOK deve ter exatamente 32 bytes.", nameof(key));

        _key = key.ToArray();
        _hmacKey = SHA256.HashData(_key);
    }

    public string Encrypt(string plainText)
    {
        if (plainText is null)
            throw new ArgumentNullException(nameof(plainText));

        var plaintextBytes = Encoding.UTF8.GetBytes(plainText);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipherBytes = new byte[plaintextBytes.Length];
        var tag = new byte[16];

        using var aes = new AesGcm(_key, tagSizeInBytes: 16);
        aes.Encrypt(nonce, plaintextBytes, cipherBytes, tag);

        return $"{VersionPrefix}:{Convert.ToBase64String(nonce)}:{Convert.ToBase64String(tag)}:{Convert.ToBase64String(cipherBytes)}";
    }

    public string? EncryptNullable(string? plainText)
    {
        if (string.IsNullOrWhiteSpace(plainText))
            return null;

        return Encrypt(plainText.Trim());
    }

    public string Decrypt(string cipherText)
    {
        if (string.IsNullOrWhiteSpace(cipherText))
            throw new ArgumentException("Texto cifrado ausente.", nameof(cipherText));

        var parts = cipherText.Split(':');
        if (parts.Length != 4 || !string.Equals(parts[0], VersionPrefix, StringComparison.Ordinal))
            throw new InvalidOperationException("Formato de texto cifrado inválido.");

        var nonce = Convert.FromBase64String(parts[1]);
        var tag = Convert.FromBase64String(parts[2]);
        var cipherBytes = Convert.FromBase64String(parts[3]);
        var plaintextBytes = new byte[cipherBytes.Length];

        using var aes = new AesGcm(_key, tagSizeInBytes: 16);
        aes.Decrypt(nonce, cipherBytes, tag, plaintextBytes);

        return Encoding.UTF8.GetString(plaintextBytes);
    }

    public string? DecryptNullable(string? cipherText)
    {
        if (string.IsNullOrWhiteSpace(cipherText))
            return null;

        return Decrypt(cipherText);
    }

    public string ComputeDocumentoHash(string documentoSomenteDigitos)
    {
        if (string.IsNullOrWhiteSpace(documentoSomenteDigitos))
            return string.Empty;

        var digits = new string(documentoSomenteDigitos.Where(char.IsDigit).ToArray());
        using var hmac = new HMACSHA256(_hmacKey);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(digits));
        return Convert.ToBase64String(hash);
    }

    public static bool TryCreateFromBase64(string? base64Key, out AesGcmClienteDataProtector? protector, out string? erro)
    {
        protector = null;
        erro = null;

        if (string.IsNullOrWhiteSpace(base64Key))
        {
            erro = "Chave BYOK ausente.";
            return false;
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(base64Key.Trim());
        }
        catch (FormatException)
        {
            erro = "Chave BYOK inválida: formato Base64 incorreto.";
            return false;
        }

        if (key.Length != 32)
        {
            erro = $"Chave BYOK inválida: esperado 32 bytes, recebido {key.Length}.";
            return false;
        }

        protector = new AesGcmClienteDataProtector(key);
        return true;
    }
}
