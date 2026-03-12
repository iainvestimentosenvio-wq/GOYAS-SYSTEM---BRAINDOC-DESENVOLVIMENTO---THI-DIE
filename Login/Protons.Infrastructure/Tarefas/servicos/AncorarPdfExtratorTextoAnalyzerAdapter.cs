using Protons.Core.Tarefas.Models;
using Protons.Core.Tarefas.Services;

namespace Protons.Infrastructure.Tarefas.Services;

public sealed class AncorarPdfExtratorTextoAnalyzerAdapter : IAncorarPdfExtratorTexto
{
    private readonly IAncorarPdfDocumentoAnalyzer _analyzer;
    private readonly Func<AncorarPdfDocumentoAnalyzerOptions> _optionsFactory;

    public AncorarPdfExtratorTextoAnalyzerAdapter(
        IAncorarPdfDocumentoAnalyzer analyzer,
        Func<AncorarPdfDocumentoAnalyzerOptions>? optionsFactory = null)
    {
        _analyzer = analyzer;
        _optionsFactory = optionsFactory ?? CriarOpcoesDoContexto;
    }

    public async Task<IReadOnlyList<PdfPaginaTexto>> ExtrairAsync(string arquivoPath, CancellationToken ct)
    {
        var analise = await _analyzer.AnalisarAsync(arquivoPath, _optionsFactory(), ct).ConfigureAwait(false);
        return analise.ToPdfPaginasTextoMescladas();
    }

    private static AncorarPdfDocumentoAnalyzerOptions CriarOpcoesDoContexto()
    {
        var config = AncorarPdfOcrConfigContext.Current;
        return new AncorarPdfDocumentoAnalyzerOptions
        {
            AnalyzerV2Ativo = true,
            HybridOcrAtivo = config?.OcrFallbackAtivo ?? false,
            OcrDpi = config?.OcrDpi ?? 300,
            OcrLang = string.IsNullOrWhiteSpace(config?.OcrLang) ? "por+eng" : config!.OcrLang
        };
    }
}
