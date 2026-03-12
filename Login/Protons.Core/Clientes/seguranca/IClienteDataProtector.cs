namespace Protons.Core.Clientes.Security;

public interface IClienteDataProtector
{
    string Encrypt(string plainText);
    string? EncryptNullable(string? plainText);
    string Decrypt(string cipherText);
    string? DecryptNullable(string? cipherText);
    string ComputeDocumentoHash(string documentoSomenteDigitos);
}
