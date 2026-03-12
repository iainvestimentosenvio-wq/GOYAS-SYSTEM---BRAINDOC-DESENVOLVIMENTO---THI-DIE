using Protons.Core.Tarefas.Models;

namespace Protons.Core.Tarefas.Services;

public interface IAncorarPdfDocumentoAnalyzer
{
    Task<AncorarPdfDocumentoAnalise> AnalisarAsync(
        string arquivoPath,
        AncorarPdfDocumentoAnalyzerOptions options,
        CancellationToken ct);

    Task<IReadOnlyList<AncorarPdfTokenAnalise>> AnalisarRegiaoAsync(
        string arquivoPath,
        AncorarPdfAnaliseRegiaoRequest request,
        AncorarPdfDocumentoAnalyzerOptions options,
        CancellationToken ct);
}
