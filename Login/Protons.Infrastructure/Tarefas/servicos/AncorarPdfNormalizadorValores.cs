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
}
