using System.Runtime.InteropServices;
using Docnet.Core;
using Docnet.Core.Models;
using Protons.Core.Tarefas.Models;
using Protons.Core.Tarefas.Services;
using SkiaSharp;
using Tesseract;

namespace Protons.Infrastructure.Tarefas.Services;

/// <summary>
/// Extrator OCR via Tesseract para PDFs escaneados (sem texto nativo).
/// Usa Docnet (PDFium) para renderizar páginas em imagem e Tesseract para reconhecimento.
/// Retorna PdfPaginaTexto com coordenadas normalizadas (0-1) compatíveis com o ancorador espacial.
/// </summary>
public sealed class AncorarPdfExtratorOcrTesseract : IAncorarPdfExtratorTexto
{
    private readonly string _tessDataPath;
    private readonly int _dpi;
    private readonly string _lang;
    private readonly IReadOnlyList<string> _langs;
    private readonly Action<string, string?>? _onLog;

    /// <param name="tessDataPath">Caminho para pasta tessdata.</param>
    /// <param name="dpi">DPI para renderização (300 recomendado).</param>
    /// <param name="lang">Idiomas Tesseract (ex: por+eng).</param>
    /// <param name="onLog">Callback opcional para log.</param>
    public AncorarPdfExtratorOcrTesseract(
        string tessDataPath,
        int dpi = 300,
        string lang = "por+eng",
        Action<string, string?>? onLog = null)
    {
        _tessDataPath = tessDataPath;
        _dpi = Math.Clamp(dpi, 150, 600);
        _langs = NormalizarIdiomas(lang);
        _lang = string.Join('+', _langs);
        _onLog = onLog;
    }

    public Task<IReadOnlyList<PdfPaginaTexto>> ExtrairAsync(string arquivoPath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ValidarDependenciasOcr();

        _onLog?.Invoke("info", $"ocr_runtime_start arquivo={arquivoPath} ocr_dpi={_dpi} ocr_lang={_lang}");
        var paginas = new List<PdfPaginaTexto>();
        var dims = new PageDimensions(_dpi / 72.0);

        using var library = DocLib.Instance;
        using var docReader = library.GetDocReader(arquivoPath, dims);
        using var engine = new TesseractEngine(_tessDataPath, _lang, EngineMode.Default);

        var numPages = docReader.GetPageCount();
        for (var i = 0; i < numPages; i++)
        {
            ct.ThrowIfCancellationRequested();

            using var pageReader = docReader.GetPageReader(i);
            var rawBytes = pageReader.GetImage();
            var width = pageReader.GetPageWidth();
            var height = pageReader.GetPageHeight();
            var pngBytes = ConverterRawParaPng(rawBytes, width, height);

            var palavras = OcrPagina(engine, pngBytes, width, height, i + 1);
            paginas.Add(new PdfPaginaTexto(i + 1, (double)width, (double)height, palavras));
        }

        return Task.FromResult<IReadOnlyList<PdfPaginaTexto>>(paginas);
    }

    private void ValidarDependenciasOcr()
    {
        if (!Directory.Exists(_tessDataPath))
        {
            _onLog?.Invoke("warn", $"ocr_tessdata_nao_encontrado path={_tessDataPath}");
            throw CriarFalhaOcrIndisponivel(
                $"tessdata não encontrado em '{_tessDataPath}'. Configure ANCORA_TESSDATA_PATH ou instale os idiomas Tesseract.");
        }

        var idiomasAusentes = _langs
            .Where(lang => !File.Exists(Path.Combine(_tessDataPath, $"{lang}.traineddata")))
            .ToList();

        if (idiomasAusentes.Count == 0)
            return;

        _onLog?.Invoke("warn",
            $"ocr_idioma_ausente path={_tessDataPath} idiomas={string.Join('+', idiomasAusentes)}");
        throw CriarFalhaOcrIndisponivel(
            $"idiomas ausentes em tessdata ({string.Join(", ", idiomasAusentes)}).");
    }

    private static IReadOnlyList<string> NormalizarIdiomas(string? lang)
    {
        var tokens = (lang ?? string.Empty)
            .Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.ToLowerInvariant())
            .ToList();

        if (tokens.Count == 0)
            tokens.AddRange(["por", "eng"]);

        var normalizados = new List<string>(tokens.Count);
        foreach (var token in tokens)
        {
            if (string.IsNullOrWhiteSpace(token))
                continue;

            if (!normalizados.Contains(token, StringComparer.Ordinal))
                normalizados.Add(token);
        }

        return normalizados;
    }

    private AncorarPdfFalhaDeNegocioException CriarFalhaOcrIndisponivel(string detalhe)
    {
        return new AncorarPdfFalhaDeNegocioException(
            AncorarPdfErroCodigos.NegOcrIndisponivel,
            $"OCR indisponível: {detalhe}");
    }

    private static byte[] ConverterRawParaPng(byte[] rawBytes, int width, int height)
    {
        using var bmp = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var ptr = bmp.GetPixels();
        if (ptr != IntPtr.Zero && rawBytes.Length == width * height * 4)
            Marshal.Copy(rawBytes, 0, ptr, rawBytes.Length);

        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(SKEncodedImageFormat.Png, quality: 90);
        return data.ToArray();
    }

    private IReadOnlyList<PdfPalavra> OcrPagina(
        TesseractEngine engine, byte[] imagePng, double larguraPt, double alturaPt, int numeroPagina)
    {
        var palavras = new List<PdfPalavra>();
        try
        {
            using var img = Pix.LoadFromMemory(imagePng);
            using var page = engine.Process(img);

            using var iterator = page.GetIterator();
            iterator.Begin();
            do
            {
                var text = iterator.GetText(PageIteratorLevel.Word);
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                if (iterator.TryGetBoundingBox(PageIteratorLevel.Word, out var rect))
                {
                    var bbox = NormalizarBbox(rect, larguraPt, alturaPt);
                    palavras.Add(new PdfPalavra(text.Trim(), bbox));
                }
            } while (iterator.Next(PageIteratorLevel.Word));
        }
        catch (Exception ex)
        {
            _onLog?.Invoke("warn", $"ocr_pagina_erro pagina={numeroPagina}: {ex.Message}");
        }

        return palavras;
    }

    private static BboxRelativo NormalizarBbox(Rect rect, double larguraPt, double alturaPt)
    {
        if (larguraPt <= 0 || alturaPt <= 0)
            return new BboxRelativo(0, 0, 0, 0);

        // Tesseract: origem top-left, coordenadas em pixels.
        // Normalizar para 0-1 (top-left).
        var x = rect.X1 / larguraPt;
        var y = rect.Y1 / alturaPt;
        var w = (rect.X2 - rect.X1) / larguraPt;
        var h = (rect.Y2 - rect.Y1) / alturaPt;

        return new BboxRelativo(
            Math.Clamp(x, 0, 1),
            Math.Clamp(y, 0, 1),
            Math.Clamp(w, 0, 1),
            Math.Clamp(h, 0, 1));
    }
}
