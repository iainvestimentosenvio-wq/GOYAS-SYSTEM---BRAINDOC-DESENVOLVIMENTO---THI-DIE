using System.Collections.Frozen;
using System.Text.RegularExpressions;

namespace Protons.Infrastructure.Tarefas.Services;

// Catálogo de regexes geradas em tempo de compilação (zero alocações em hot path).
internal static partial class AncorarPdfRegexCatalog // internal: usado apenas dentro da infra
{
    // CPF: 000.000.000-00 ou 00000000000 (11 dígitos com ou sem pontuação).
    [GeneratedRegex(@"\b(\d{3}\.?\d{3}\.?\d{3}-?\d{2})\b")]
    public static partial Regex CpfPattern();

    // CNPJ: 00.000.000/0000-00 ou 00000000000100 (14 dígitos com ou sem pontuação).
    [GeneratedRegex(@"\b(\d{2}\.?\d{3}\.?\d{3}\/?\d{4}-?\d{2})\b")]
    public static partial Regex CnpjPattern();

    // Remove não-dígitos: substitui qualquer char que não seja dígito (0–9).
    // Uso: NormalizarInteiro, NormalizarCpf/Cnpj após extração bruta.
    [GeneratedRegex(@"[^\d]")]
    public static partial Regex DigitsOnlyPattern();

    // Extrai valor monetário BRL: captura parte numérica de "R$ 1.234,56" ou "1.234,56".
    [GeneratedRegex(@"R?\$?\s*([\d.,]+)", RegexOptions.IgnoreCase)]
    public static partial Regex MoedaBrlPattern();

    // E-mail: endereço de e-mail com @ e domínio.
    [GeneratedRegex(@"[\w.+\-]+@[\w\-]+\.[\w.\-]+")]
    public static partial Regex EmailPattern();

    // CEP com hífen: 00000-000.
    [GeneratedRegex(@"\b\d{5}-\d{3}\b")]
    public static partial Regex CepComHifenPattern();

    // CEP sem hífen: 8 dígitos consecutivos.
    [GeneratedRegex(@"\b\d{8}\b")]
    public static partial Regex CepSemHifenPattern();

    // Telefone BR: (DD) 0000-0000 ou (DD) 00000-0000, com variações de formatação.
    [GeneratedRegex(@"\(?\d{2}\)?\s*\d{4,5}[-\s]?\d{4}")]
    public static partial Regex TelefoneBrPattern();

    // Data brasileira: DD/MM/AAAA.
    [GeneratedRegex(@"\b\d{2}/\d{2}/\d{4}\b")]
    public static partial Regex DataBrPattern();

    // Data por extenso em PT-BR: "27 de outubro de 2025" ou "5 de março de 2026".
    [GeneratedRegex(@"\b\d{1,2}\s+de\s+\w+\s+de\s+\d{4}\b", RegexOptions.IgnoreCase)]
    public static partial Regex DataExtensoPattern();

    // Remove pontuação de CPF/CNPJ: pontos, traços, barras.
    public static string Normalizar(string raw)
        => raw.Replace(".", "").Replace("-", "").Replace("/", "").Trim();

    // Conjunto imutável de siglas de estados brasileiros para validação de UF.
    public static readonly FrozenSet<string> UfsBrasil =
        new string[]
        {
            "AC", "AL", "AP", "AM", "BA", "CE", "DF", "ES", "GO", "MA", "MT", "MS",
            "MG", "PA", "PB", "PR", "PE", "PI", "RJ", "RN", "RS", "RO", "RR", "SC",
            "SP", "SE", "TO"
        }.ToFrozenSet(StringComparer.Ordinal);
}
