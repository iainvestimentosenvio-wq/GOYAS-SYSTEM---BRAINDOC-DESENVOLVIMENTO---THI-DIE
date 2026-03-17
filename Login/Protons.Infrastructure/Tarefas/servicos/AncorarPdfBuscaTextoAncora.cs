using Protons.Core.Tarefas.Models;
using Protons.Core.Tarefas.Services;

namespace Protons.Infrastructure.Tarefas.Services;

/// <summary>
/// Busca um fragmento de texto na página e retorna o bbox da ocorrência solicitada.
/// Agrupa palavras por linha (Y dentro de delta) para evitar falsos positivos em layouts multi-coluna.
/// Suporta busca fuzzy (Contains parcial) e seleção de N-ésima ocorrência.
/// </summary>
public sealed class AncorarPdfBuscaTextoAncora : IAncorarPdfBuscaTextoAncora
{
    /// <summary>Delta relativo de Y para considerar palavras na mesma linha (0.5% da altura).</summary>
    private const double DeltaLinhaRel = 0.005;

    /// <summary>Limiar de similaridade para busca fuzzy (70%).</summary>
    private const double LimiarFuzzy = 0.70;

    public BboxRelativo? Buscar(PdfPaginaTexto pagina, string textoAncora)
        => Buscar(pagina, textoAncora, ocorrencia: 1);

    /// <summary>
    /// Busca a N-ésima ocorrência do texto na página.
    /// Tenta primeiro match exato (Contains), depois fuzzy (similaridade ≥ 70%).
    /// </summary>
    public BboxRelativo? Buscar(PdfPaginaTexto pagina, string textoAncora, int ocorrencia)
    {
        if (string.IsNullOrWhiteSpace(textoAncora))
            return null;

        var textoNormalizado = NormalizarTexto(textoAncora);
        if (string.IsNullOrEmpty(textoNormalizado))
            return null;

        var palavras = pagina.Palavras
            .OrderBy(p => p.Bbox.Y)
            .ThenBy(p => p.Bbox.X)
            .ToList();

        if (palavras.Count == 0)
            return null;

        var linhas = AgruparPorLinha(palavras);
        var targetOcorrencia = Math.Max(1, ocorrencia);

        var encontradas = 0;
        foreach (var linha in linhas)
        {
            var resultado = BuscarNaLinha(linha, textoNormalizado);
            if (resultado is not null && ++encontradas == targetOcorrencia)
                return resultado;
        }

        if (encontradas > 0)
            return null;

        foreach (var linha in linhas)
        {
            var resultado = BuscarFuzzyNaLinha(linha, textoNormalizado);
            if (resultado is not null)
                return resultado;
        }

        return null;
    }

    private static IReadOnlyList<IReadOnlyList<PdfPalavra>> AgruparPorLinha(List<PdfPalavra> palavras)
    {
        if (palavras.Count == 0)
            return [];

        var linhas = new List<List<PdfPalavra>>();
        var linhaAtual = new List<PdfPalavra> { palavras[0] };
        var yBase = palavras[0].Bbox.Y;

        for (var i = 1; i < palavras.Count; i++)
        {
            var p = palavras[i];
            var deltaY = Math.Abs(p.Bbox.Y - yBase);
            if (deltaY <= DeltaLinhaRel)
            {
                linhaAtual.Add(p);
            }
            else
            {
                linhas.Add(linhaAtual);
                linhaAtual = [p];
                yBase = p.Bbox.Y;
            }
        }

        linhas.Add(linhaAtual);
        return linhas;
    }

    private static BboxRelativo? BuscarNaLinha(IReadOnlyList<PdfPalavra> linha, string textoNormalizado)
    {
        for (var i = 0; i < linha.Count; i++)
        {
            var textoAcumulado = string.Empty;
            var palavrasMatch = new List<PdfPalavra>();

            for (var j = i; j < linha.Count; j++)
            {
                var p = linha[j];
                textoAcumulado = string.IsNullOrEmpty(textoAcumulado)
                    ? NormalizarTexto(p.Texto)
                    : textoAcumulado + " " + NormalizarTexto(p.Texto);
                palavrasMatch.Add(p);

                if (textoAcumulado.Contains(textoNormalizado, StringComparison.OrdinalIgnoreCase))
                    return BboxEnvolvente(palavrasMatch);

                if (textoAcumulado.Length > textoNormalizado.Length * 2)
                    break;
            }
        }

        return null;
    }

    private static BboxRelativo? BuscarFuzzyNaLinha(IReadOnlyList<PdfPalavra> linha, string textoNormalizado)
    {
        for (var i = 0; i < linha.Count; i++)
        {
            var textoAcumulado = string.Empty;
            var palavrasMatch = new List<PdfPalavra>();

            for (var j = i; j < linha.Count; j++)
            {
                var p = linha[j];
                textoAcumulado = string.IsNullOrEmpty(textoAcumulado)
                    ? NormalizarTexto(p.Texto)
                    : textoAcumulado + " " + NormalizarTexto(p.Texto);
                palavrasMatch.Add(p);

                var sim = CalcularSimilaridade(textoAcumulado, textoNormalizado);
                if (sim >= LimiarFuzzy)
                    return BboxEnvolvente(palavrasMatch);

                if (textoAcumulado.Length > textoNormalizado.Length * 3)
                    break;
            }
        }

        return null;
    }

    /// <summary>
    /// Similaridade baseada em tokens comuns (Jaccard sobre palavras).
    /// Rápida, sem alocação pesada, suficiente para labels de documentos.
    /// </summary>
    private static double CalcularSimilaridade(string a, string b)
    {
        var tokensA = a.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var tokensB = b.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokensA.Length == 0 || tokensB.Length == 0)
            return 0.0;

        var intersecao = 0;
        foreach (var ta in tokensA)
        {
            foreach (var tb in tokensB)
            {
                if (string.Equals(ta, tb, StringComparison.OrdinalIgnoreCase))
                {
                    intersecao++;
                    break;
                }
            }
        }

        var uniao = tokensA.Length + tokensB.Length - intersecao;
        return uniao > 0 ? (double)intersecao / uniao : 0.0;
    }

    private static string NormalizarTexto(string texto)
    {
        var chars = new[] { ' ', '\t', '\n', '\r' };
        return string.Join(" ", texto.Split(chars, StringSplitOptions.RemoveEmptyEntries)).Trim();
    }

    private static BboxRelativo BboxEnvolvente(List<PdfPalavra> palavras)
    {
        var xMin = double.MaxValue;
        var yMin = double.MaxValue;
        var xMax = double.MinValue;
        var yMax = double.MinValue;

        foreach (var p in palavras)
        {
            if (p.Bbox.X < xMin) xMin = p.Bbox.X;
            if (p.Bbox.Y < yMin) yMin = p.Bbox.Y;
            var px = p.Bbox.X + p.Bbox.Largura;
            if (px > xMax) xMax = px;
            var py = p.Bbox.Y + p.Bbox.Altura;
            if (py > yMax) yMax = py;
        }

        return new BboxRelativo(xMin, yMin, xMax - xMin, yMax - yMin);
    }
}
