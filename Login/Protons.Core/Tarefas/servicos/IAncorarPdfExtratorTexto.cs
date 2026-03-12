using Protons.Core.Tarefas.Models;

namespace Protons.Core.Tarefas.Services;

// Extrai texto estruturado com coordenadas bbox de um arquivo PDF.
// PDF inacessível → lança IOException (Polly no worker retenta).
// PDF sem texto nativo → retorna páginas com Palavras vazias.
public interface IAncorarPdfExtratorTexto
{
    Task<IReadOnlyList<PdfPaginaTexto>> ExtrairAsync(string arquivoPath, CancellationToken ct);
}
