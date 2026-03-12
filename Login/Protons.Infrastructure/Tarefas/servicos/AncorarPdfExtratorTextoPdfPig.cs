using Protons.Core.Tarefas.Models;
using Protons.Core.Tarefas.Services;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Protons.Infrastructure.Tarefas.Services;

// Extrai texto com coordenadas normalizadas de PDFs nativos (não escaneados).
// Usa UglyToad.PdfPig 0.1.13 — zero dependências transitivas.
// Coordenadas PdfPig: origem bottom-left em pontos → normalizadas para top-left 0.0–1.0.
public sealed class AncorarPdfExtratorTextoPdfPig : IAncorarPdfExtratorTexto
{
    public Task<IReadOnlyList<PdfPaginaTexto>> ExtrairAsync(string arquivoPath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // PdfDocument.Open pode lançar IOException para arquivos inacessíveis
        // (Polly no worker retenta). Mantemos a exceção propagar.
        using var doc = PdfDocument.Open(arquivoPath);

        var paginas = new List<PdfPaginaTexto>(doc.NumberOfPages);
        foreach (var page in doc.GetPages())
        {
            ct.ThrowIfCancellationRequested();
            var largura = page.Width;
            var altura = page.Height;

            // Capacidade inicial 256: evita resize nas primeiras iterações (página típica: 100–400 palavras).
            var palavras = new List<PdfPalavra>(256);
            foreach (var word in page.GetWords())
            {
                var bbox = NormalizarBbox(word.BoundingBox, largura, altura);
                palavras.Add(new PdfPalavra(word.Text, bbox));
            }

            paginas.Add(new PdfPaginaTexto(page.Number, largura, altura, palavras));
        }

        return Task.FromResult<IReadOnlyList<PdfPaginaTexto>>(paginas);
    }

    // Converte BoundingBox PdfPig (bottom-left origin, pontos) para BboxRelativo (top-left, 0.0–1.0).
    // PdfPig: Y cresce para cima. Top-left: Y = (altura - bottomY - boxAltura) / altura
    private static BboxRelativo NormalizarBbox(
        UglyToad.PdfPig.Core.PdfRectangle rect, double largura, double altura)
    {
        if (largura <= 0 || altura <= 0)
            return new BboxRelativo(0, 0, 0, 0);

        var x = rect.Left / largura;
        var w = rect.Width / largura;
        // PdfPig Bottom = rect.Bottom (menor Y), Top = rect.Top (maior Y)
        var y = (altura - rect.Top) / altura;
        var h = rect.Height / altura;

        return new BboxRelativo(
            Math.Clamp(x, 0, 1),
            Math.Clamp(y, 0, 1),
            Math.Clamp(w, 0, 1),
            Math.Clamp(h, 0, 1));
    }
}
