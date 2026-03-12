using Protons.Core.Tarefas.Models;
using Protons.Core.Tarefas.Services;

namespace Protons.Infrastructure.Tarefas.Services;

// Ancora variáveis por interseção bbox: filtra palavras cujo bbox intersecta
// a região relativa da âncora do template e produz SaidaVariavelItem por âncora.
// Suporta modo RegiaoFixa (comportamento atual) e TextoADireita/TextoAbaixo (buscar texto e extrair relativamente).
public sealed class AncorarPdfAncoradorEspacialBbox : IAncorarPdfAncoradorEspacial
{
    private readonly IAncorarPdfBuscaTextoAncora _buscaTexto;

    public AncorarPdfAncoradorEspacialBbox(IAncorarPdfBuscaTextoAncora? buscaTexto = null)
    {
        _buscaTexto = buscaTexto ?? new AncorarPdfBuscaTextoAncora();
    }

    public IReadOnlyList<SaidaVariavelItem> Ancorar(
        IReadOnlyList<PdfPaginaTexto> paginas,
        IReadOnlyList<AncorarPdfTemplateAncora> ancoras)
    {
        var resultado = new List<SaidaVariavelItem>(ancoras.Count);

        foreach (var ancora in ancoras.OrderBy(a => a.Ordem))
        {
            // Seleciona página pela âncora (1-indexed).
            var paginaIdx = ancora.Pagina - 1;
            PdfPaginaTexto? pagina = null;
            if (paginaIdx >= 0 && paginaIdx < paginas.Count)
                pagina = paginas[paginaIdx];

            BboxRelativo regiaoExtracao;
            if (ancora.ModoAncora == AncorarPdfModoAncora.RegiaoFixa || string.IsNullOrWhiteSpace(ancora.TextoAncora))
            {
                regiaoExtracao = new BboxRelativo(ancora.XRel, ancora.YRel, ancora.LarguraRel, ancora.AlturaRel);
            }
            else if (pagina is null)
            {
                resultado.Add(CriarItemVazio(ancora));
                continue;
            }
            else
            {
                var bboxAncora = _buscaTexto.Buscar(pagina, ancora.TextoAncora);
                if (bboxAncora is null)
                {
                    resultado.Add(CriarItemVazio(ancora));
                    continue;
                }
                regiaoExtracao = CalcularRegiaoRelativa(bboxAncora, ancora);
            }

            var palavrasEncontradas = new List<PdfPalavra>();
            if (pagina is not null)
            {
                foreach (var palavra in pagina.Palavras)
                {
                    if (Intersecta(palavra.Bbox, regiaoExtracao))
                        palavrasEncontradas.Add(palavra);
                }
            }

            // Ordena por Y asc (linha de cima para baixo), depois X asc (esquerda para direita).
            var ordenadas = palavrasEncontradas
                .OrderBy(p => p.Bbox.Y)
                .ThenBy(p => p.Bbox.X)
                .ToList();

            var valorBruto = string.Join(" ", ordenadas.Select(p => p.Texto)).Trim();
            var valorNorm = AncorarPdfNormalizadorValores.Normalizar(
                valorBruto, ancora.Metadado.RegraNormalizacao);

            // Confiança: ≥1 palavra encontrada = plena; 0 palavras = 0.
            var confianca = palavrasEncontradas.Count > 0 ? 1.0 : 0.0;

            // Bbox consolidado: envolvente de todas as palavras encontradas (ou âncora como fallback).
            BboxRelativo? bboxSaida = palavrasEncontradas.Count > 0
                ? BboxEnvolvente(ordenadas)
                : null;

            resultado.Add(new SaidaVariavelItem
            {
                Chave = ancora.Metadado.ChaveTecnica,
                Tipo = ancora.Metadado.TipoEsperado,
                ValorBruto = valorBruto,
                ValorNormalizado = valorNorm,
                Confianca = confianca,
                CorTemplate = ancora.CorHex,
                Pagina = ancora.Pagina,
                BboxRelativo = bboxSaida
            });
        }

        return resultado;
    }

    // Teste de interseção entre dois retângulos normalizados (0.0–1.0).
    // Retorna false se forem disjuntos em qualquer eixo.
    private static bool Intersecta(BboxRelativo a, BboxRelativo b)
    {
        return !(a.X + a.Largura <= b.X ||
                 b.X + b.Largura <= a.X ||
                 a.Y + a.Altura <= b.Y ||
                 b.Y + b.Altura <= a.Y);
    }

    // Calcula bbox envolvente de uma lista de palavras em passagem única (O(n), sem LINQ).
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

    private static SaidaVariavelItem CriarItemVazio(AncorarPdfTemplateAncora ancora)
    {
        return new SaidaVariavelItem
        {
            Chave = ancora.Metadado.ChaveTecnica,
            Tipo = ancora.Metadado.TipoEsperado,
            ValorBruto = string.Empty,
            ValorNormalizado = string.Empty,
            Confianca = 0.0,
            CorTemplate = ancora.CorHex,
            Pagina = ancora.Pagina,
            BboxRelativo = null
        };
    }

    private static BboxRelativo CalcularRegiaoRelativa(BboxRelativo bboxAncora, AncorarPdfTemplateAncora ancora)
    {
        return ancora.ModoAncora switch
        {
            AncorarPdfModoAncora.TextoADireita => ClampRegiao(
                bboxAncora.X + bboxAncora.Largura,
                bboxAncora.Y,
                ancora.LarguraExtracaoRel,
                ancora.AlturaExtracaoRel),
            AncorarPdfModoAncora.TextoAbaixo => ClampRegiao(
                bboxAncora.X,
                bboxAncora.Y + bboxAncora.Altura,
                ancora.LarguraExtracaoRel,
                ancora.AlturaExtracaoRel),
            _ => new BboxRelativo(ancora.XRel, ancora.YRel, ancora.LarguraRel, ancora.AlturaRel)
        };
    }

    /// <summary>Clampa região ao intervalo [0,1] para evitar extrapolação nas bordas da página.</summary>
    private static BboxRelativo ClampRegiao(double x, double y, double largura, double altura)
    {
        var xClamp = Math.Clamp(x, 0, 1);
        var yClamp = Math.Clamp(y, 0, 1);
        var larguraClamp = Math.Min(largura, 1 - xClamp);
        var alturaClamp = Math.Min(altura, 1 - yClamp);
        return new BboxRelativo(xClamp, yClamp, Math.Max(0, larguraClamp), Math.Max(0, alturaClamp));
    }
}
