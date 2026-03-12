using Protons.Core.Tarefas.Models;
using Protons.Core.Tarefas.Services;

namespace Protons.Infrastructure.Tarefas.Services;

/// <summary>
/// Decorator: usa extrator base (PdfPig); se totalPalavras == 0 e config.OcrFallbackAtivo,
/// tenta OCR via extrator secundário (lê config de AncorarPdfOcrConfigContext).
/// </summary>
public sealed class AncorarPdfExtratorTextoComOcrFallback : IAncorarPdfExtratorTexto
{
    private readonly IAncorarPdfExtratorTexto _extratorBase;
    private readonly Func<int, string, IAncorarPdfExtratorTexto> _ocrFactory;
    private readonly Action<string, string?>? _onLog;

    /// <param name="extratorBase">Extrator nativo (PdfPig).</param>
    /// <param name="ocrFactory">Factory: (dpi, lang) => extrator OCR.</param>
    public AncorarPdfExtratorTextoComOcrFallback(
        IAncorarPdfExtratorTexto extratorBase,
        Func<int, string, IAncorarPdfExtratorTexto> ocrFactory,
        Action<string, string?>? onLog = null)
    {
        _extratorBase = extratorBase;
        _ocrFactory = ocrFactory;
        _onLog = onLog;
    }

    public async Task<IReadOnlyList<PdfPaginaTexto>> ExtrairAsync(string arquivoPath, CancellationToken ct)
    {
        var paginas = await _extratorBase.ExtrairAsync(arquivoPath, ct).ConfigureAwait(false);
        var total = paginas.Sum(p => p.Palavras.Count);

        if (total > 0)
            return paginas;

        var config = AncorarPdfOcrConfigContext.Current;
        if (config is null || !config.OcrFallbackAtivo)
            return paginas;

        _onLog?.Invoke("info",
            $"ocr_fallback_attempt arquivo={arquivoPath} ocr_enabled=true ocr_dpi={config.OcrDpi} ocr_lang={config.OcrLang}");

        try
        {
            var ocr = _ocrFactory(config.OcrDpi, config.OcrLang);
            var paginasOcr = await ocr.ExtrairAsync(arquivoPath, ct).ConfigureAwait(false);
            var totalOcr = paginasOcr.Sum(p => p.Palavras.Count);

            _onLog?.Invoke("info",
                $"ocr_fallback_{(totalOcr > 0 ? "success" : "no_text")} arquivo={arquivoPath} palavras={totalOcr}");

            return paginasOcr;
        }
        catch (AncorarPdfFalhaDeNegocioException ex) when (
            string.Equals(ex.Codigo, AncorarPdfErroCodigos.NegOcrIndisponivel, StringComparison.Ordinal))
        {
            _onLog?.Invoke("warn",
                $"ocr_fallback_unavailable arquivo={arquivoPath} code={ex.Codigo} detalhe={ex.Message}");
            throw;
        }
    }
}
