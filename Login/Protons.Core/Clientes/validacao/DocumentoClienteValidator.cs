using Protons.Core.Clientes.Models;
using Protons.Core.Login.Validation;

namespace Protons.Core.Clientes.Validation;

public enum StatusAnaliseDocumentoCliente
{
    Incompleto = 0,
    Invalido = 1,
    Valido = 2
}

public sealed record AnaliseDocumentoCliente(
    StatusAnaliseDocumentoCliente Status,
    TipoDocumentoCliente? TipoDocumento,
    string DocumentoSomenteDigitos,
    bool CaracteresPermitidos);

public static class DocumentoClienteValidator
{
    private static readonly HashSet<char> SeparadoresPermitidos = ['.', '-', '/', ' '];

    public static string ExtrairSomenteDigitos(string? documento)
    {
        if (string.IsNullOrWhiteSpace(documento))
            return string.Empty;

        return new string(documento.Where(char.IsDigit).ToArray());
    }

    public static bool PossuiSomenteCaracteresPermitidos(string? documento)
    {
        if (string.IsNullOrWhiteSpace(documento))
            return true;

        foreach (var ch in documento)
        {
            if (char.IsDigit(ch) || SeparadoresPermitidos.Contains(ch))
                continue;

            return false;
        }

        return true;
    }

    public static TipoDocumentoCliente? DetectarTipoPorDocumento(string? documento)
    {
        var digits = ExtrairSomenteDigitos(documento);
        return digits.Length switch
        {
            11 => TipoDocumentoCliente.CPF,
            14 => TipoDocumentoCliente.CNPJ,
            _ => null
        };
    }

    public static AnaliseDocumentoCliente Analisar(string? documento)
    {
        var caracteresPermitidos = PossuiSomenteCaracteresPermitidos(documento);
        var digits = ExtrairSomenteDigitos(documento);
        if (!caracteresPermitidos)
        {
            return new AnaliseDocumentoCliente(
                StatusAnaliseDocumentoCliente.Invalido,
                TipoDocumento: null,
                DocumentoSomenteDigitos: digits,
                CaracteresPermitidos: false);
        }

        if (digits.Length == 0 || digits.Length < 11 || (digits.Length > 11 && digits.Length < 14))
        {
            return new AnaliseDocumentoCliente(
                StatusAnaliseDocumentoCliente.Incompleto,
                TipoDocumento: null,
                DocumentoSomenteDigitos: digits,
                CaracteresPermitidos: true);
        }

        if (digits.Length != 11 && digits.Length != 14)
        {
            return new AnaliseDocumentoCliente(
                StatusAnaliseDocumentoCliente.Invalido,
                TipoDocumento: null,
                DocumentoSomenteDigitos: digits,
                CaracteresPermitidos: true);
        }

        var tipo = digits.Length == 11 ? TipoDocumentoCliente.CPF : TipoDocumentoCliente.CNPJ;
        var valido = EhValido(tipo, digits);
        return new AnaliseDocumentoCliente(
            valido ? StatusAnaliseDocumentoCliente.Valido : StatusAnaliseDocumentoCliente.Invalido,
            TipoDocumento: tipo,
            DocumentoSomenteDigitos: digits,
            CaracteresPermitidos: true);
    }

    public static bool EhValido(TipoDocumentoCliente tipoDocumento, string? documento)
    {
        var digits = ExtrairSomenteDigitos(documento);
        return tipoDocumento switch
        {
            TipoDocumentoCliente.CPF => CpfValidator.EhValido(digits),
            TipoDocumentoCliente.CNPJ => CnpjValidator.EhValido(digits),
            _ => false
        };
    }

    public static string AplicarMascaraParcial(string? documento)
    {
        var digits = ExtrairSomenteDigitos(documento);
        if (digits.Length == 0)
            return string.Empty;

        if (digits.Length > ClienteInputLimits.MaxDocumento)
            digits = digits[..ClienteInputLimits.MaxDocumento];

        return digits.Length <= 11
            ? AplicarMascaraCpfParcial(digits)
            : AplicarMascaraCnpjParcial(digits);
    }

    public static string FormatarDocumentoCompleto(TipoDocumentoCliente tipoDocumento, string? documento)
    {
        var digits = ExtrairSomenteDigitos(documento);
        if (tipoDocumento == TipoDocumentoCliente.CPF && digits.Length == 11)
            return AplicarMascaraCpfParcial(digits);

        if (tipoDocumento == TipoDocumentoCliente.CNPJ && digits.Length == 14)
            return AplicarMascaraCnpjParcial(digits);

        return digits;
    }

    private static string AplicarMascaraCpfParcial(string digits)
    {
        if (digits.Length <= 3)
            return digits;

        if (digits.Length <= 6)
            return $"{digits[..3]}.{digits[3..]}";

        if (digits.Length <= 9)
            return $"{digits[..3]}.{digits.Substring(3, 3)}.{digits[6..]}";

        return $"{digits[..3]}.{digits.Substring(3, 3)}.{digits.Substring(6, 3)}-{digits[9..]}";
    }

    private static string AplicarMascaraCnpjParcial(string digits)
    {
        if (digits.Length <= 2)
            return digits;

        if (digits.Length <= 5)
            return $"{digits[..2]}.{digits[2..]}";

        if (digits.Length <= 8)
            return $"{digits[..2]}.{digits.Substring(2, 3)}.{digits[5..]}";

        if (digits.Length <= 12)
            return $"{digits[..2]}.{digits.Substring(2, 3)}.{digits.Substring(5, 3)}/{digits[8..]}";

        return $"{digits[..2]}.{digits.Substring(2, 3)}.{digits.Substring(5, 3)}/{digits.Substring(8, 4)}-{digits[12..]}";
    }
}
