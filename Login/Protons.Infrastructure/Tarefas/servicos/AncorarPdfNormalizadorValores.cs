using System.Globalization;

namespace Protons.Infrastructure.Tarefas.Services;

// Aplica regras de normalização por tipo de variável extraída.
// Usa AncorarPdfRegexCatalog para regexes source-generated (zero alocações em hot path).
internal static class AncorarPdfNormalizadorValores
{

    /// <summary>
    /// Aplica a regra de normalização ao valor bruto extraído do PDF.
    /// regra: "cpf" | "cnpj" | "moeda_brl" | "data_br" | "inteiro" | null → texto limpo
    /// </summary>
    public static string Normalizar(string valorBruto, string? regra)
    {
        if (string.IsNullOrWhiteSpace(valorBruto))
            return string.Empty;

        return regra?.ToLowerInvariant() switch
        {
            "cpf" => NormalizarCpf(valorBruto),
            "cnpj" => NormalizarCnpj(valorBruto),
            "moeda_brl" => NormalizarMoedaBrl(valorBruto),
            "data_br" => NormalizarDataBr(valorBruto),
            "inteiro" => NormalizarInteiro(valorBruto),
            _ => valorBruto.Trim()
        };
    }

    // Remove pontuação do CPF → somente 11 dígitos.
    private static string NormalizarCpf(string valor)
        => AncorarPdfRegexCatalog.Normalizar(valor);

    // Remove pontuação do CNPJ → somente 14 dígitos.
    private static string NormalizarCnpj(string valor)
        => AncorarPdfRegexCatalog.Normalizar(valor);

    // Converte "R$ 1.234,56" → "1234.56" (padrão decimal invariante).
    private static string NormalizarMoedaBrl(string valor)
    {
        var m = AncorarPdfRegexCatalog.MoedaBrlPattern().Match(valor);
        var raw = m.Success ? m.Groups[1].Value : valor;
        // Formato BR: ponto = separador de milhar, vírgula = decimal.
        // Remove pontos de milhar e substitui vírgula decimal por ponto.
        var semMilhar = raw.Replace(".", "");
        var comPontoDecimal = semMilhar.Replace(",", ".");
        if (decimal.TryParse(comPontoDecimal, NumberStyles.Any,
            CultureInfo.InvariantCulture, out var val))
            return val.ToString("F2", CultureInfo.InvariantCulture);
        return valor.Trim();
    }

    // Tenta normalizar data brasileira "DD/MM/AAAA" → "AAAA-MM-DD" (ISO 8601).
    private static string NormalizarDataBr(string valor)
    {
        var v = valor.Trim();
        if (DateTime.TryParseExact(v, "dd/MM/yyyy",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        // Fallback: retornar como está
        return v;
    }

    // Remove não-dígitos e retorna string de inteiro.
    private static string NormalizarInteiro(string valor)
    {
        var digits = AncorarPdfRegexCatalog.DigitsOnlyPattern().Replace(valor.Trim(), "");
        return string.IsNullOrEmpty(digits) ? valor.Trim() : digits;
    }

    /// <summary>
    /// Valida se o valor normalizado é compatível com o tipo esperado.
    /// Retorna confiança entre 0.0 e 1.0 baseada na qualidade da validação.
    /// </summary>
    public static double ValidarConfianca(string valorNormalizado, string? tipo)
    {
        if (string.IsNullOrWhiteSpace(valorNormalizado))
            return 0.0;

        return tipo?.ToLowerInvariant() switch
        {
            "cpf" => ValidarCpfConfianca(valorNormalizado),
            "cnpj" => ValidarCnpjConfianca(valorNormalizado),
            "moeda_brl" => ValidarMoedaConfianca(valorNormalizado),
            "data_br" => ValidarDataConfianca(valorNormalizado),
            "inteiro" => ValidarInteiroConfianca(valorNormalizado),
            "email" => valorNormalizado.Contains('@') ? 0.95 : 0.3,
            "cep" => valorNormalizado.Length == 8 && valorNormalizado.All(char.IsDigit) ? 0.9 : 0.3,
            "telefone" => valorNormalizado.Length is >= 10 and <= 11 && valorNormalizado.All(char.IsDigit) ? 0.9 : 0.3,
            _ => 0.8
        };
    }

    private static double ValidarCpfConfianca(string valor)
    {
        var digits = new string(valor.Where(char.IsDigit).ToArray());
        if (digits.Length != 11) return 0.2;
        if (digits.Distinct().Count() == 1) return 0.1;
        return VerificarDigitosCpf(digits) ? 1.0 : 0.4;
    }

    private static double ValidarCnpjConfianca(string valor)
    {
        var digits = new string(valor.Where(char.IsDigit).ToArray());
        if (digits.Length != 14) return 0.2;
        if (digits.Distinct().Count() == 1) return 0.1;
        return VerificarDigitosCnpj(digits) ? 1.0 : 0.4;
    }

    private static double ValidarMoedaConfianca(string valor)
    {
        if (decimal.TryParse(valor, NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
            return v >= 0 ? 0.95 : 0.7;
        return 0.3;
    }

    private static double ValidarDataConfianca(string valor)
    {
        if (DateTime.TryParseExact(valor, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return dt.Year is >= 1900 and <= 2100 ? 0.95 : 0.5;
        return 0.3;
    }

    private static double ValidarInteiroConfianca(string valor)
    {
        return long.TryParse(valor, out _) ? 0.9 : 0.3;
    }

    private static bool VerificarDigitosCpf(string digits)
    {
        int[] mult1 = [10, 9, 8, 7, 6, 5, 4, 3, 2];
        int[] mult2 = [11, 10, 9, 8, 7, 6, 5, 4, 3, 2];

        var soma = 0;
        for (var i = 0; i < 9; i++)
            soma += (digits[i] - '0') * mult1[i];
        var resto = soma % 11;
        var dig1 = resto < 2 ? 0 : 11 - resto;
        if (digits[9] - '0' != dig1) return false;

        soma = 0;
        for (var i = 0; i < 10; i++)
            soma += (digits[i] - '0') * mult2[i];
        resto = soma % 11;
        var dig2 = resto < 2 ? 0 : 11 - resto;
        return digits[10] - '0' == dig2;
    }

    private static bool VerificarDigitosCnpj(string digits)
    {
        int[] mult1 = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
        int[] mult2 = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];

        var soma = 0;
        for (var i = 0; i < 12; i++)
            soma += (digits[i] - '0') * mult1[i];
        var resto = soma % 11;
        var dig1 = resto < 2 ? 0 : 11 - resto;
        if (digits[12] - '0' != dig1) return false;

        soma = 0;
        for (var i = 0; i < 13; i++)
            soma += (digits[i] - '0') * mult2[i];
        resto = soma % 11;
        var dig2 = resto < 2 ? 0 : 11 - resto;
        return digits[13] - '0' == dig2;
    }
}
