using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Docnet.Core;
using Docnet.Core.Models;
using Protons.Core.Tarefas.Services;
using SkiaSharp;

namespace Protons.Infrastructure.Tarefas.Services;

/// <summary>
/// Renderer baseado no PDFium via Docnet.Core — sem dependência de instalação externa.
/// <para>
/// Vantagens: bibliotecas nativas PDFium incluídas no NuGet, alta fidelidade,
/// suporte a DPI arbitrário até 1200, renderização síncrona de alta performance.
/// Desvantagens: PDFium não inclui o motor de renderização de fontes Type1/CFF
/// do Ghostscript; pode diferir em renderização de texto em PDFs antigos.
/// </para>
/// <para>
/// O método <see cref="RenderizarAsync"/> é assíncrono na assinatura mas síncrono na
/// execução (Docnet.Core não é async). O trabalho pesado é encapsulado em
/// <see cref="Task.FromResult{T}"/> para preservar a interface uniforme com outros renderers.
/// Para PDFs grandes, considere envolver em <c>Task.Run</c> no nível do chamador.
/// </para>
/// </summary>
public sealed class AncorarPdfDocnetRenderer : IPdfPreviewRenderer
{
    // A4 em pontos (1 pt = 1/72 in). Usado para calcular dimensões de render.
    // PDFium escala a página para caber nas dimensões fornecidas, mantendo aspect ratio.
    private const double PtsPorPolegada = 72.0;

    private static readonly PdfRendererCapabilities _capabilities = new(
        SuportaLinux: true,
        SuportaWindows: true,
        SuportaMacOs: true,
        DpiMinimo: 72,
        DpiMaximo: 1200,
        RequereExternoInstalado: false);

    public string NomeRenderer => "Docnet.PDFium";
    public PdfRendererCapabilities Capabilities => _capabilities;

    /// <summary>
    /// Verifica se as bibliotecas nativas do PDFium estão acessíveis.
    /// A primeira chamada a <see cref="DocLib.Instance"/> carrega as nativas;
    /// se falhar, retorna <c>false</c>.
    /// </summary>
    public bool Disponivel
    {
        get
        {
            try
            {
                _ = DocLib.Instance;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <inheritdoc/>
    public Task<PdfRenderResult> RenderizarAsync(
        PdfRenderRequest request,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var utcInicio = DateTimeOffset.UtcNow;
        int dpi = request.DpiNormalizado;

        if (!File.Exists(request.PdfPath))
        {
            return Task.FromResult<PdfRenderResult>(Falhou(
                "Arquivo não encontrado.", "DOCNET_FILE_NOT_FOUND",
                dpi, sw.ElapsedMilliseconds, utcInicio));
        }

        try
        {
            ct.ThrowIfCancellationRequested();

            // Dimensão máxima em pixels para o DPI solicitado.
            // Tomamos como referência o lado longo do A3 (420 mm = 1189 pt ≈ 16,52 in),
            // o que garante espaço suficiente para páginas A4, Carta e A3, portrait e landscape.
            // PDFium escala a página para caber em (maxDim × maxDim) mantendo aspect ratio.
            int maxDim = (int)Math.Ceiling(1189.0 * dpi / PtsPorPolegada);

            using var docReader = DocLib.Instance.GetDocReader(
                request.PdfPath,
                new PageDimensions(maxDim, maxDim));

            int pageIndex = Math.Max(0, request.Pagina - 1);
            using var pageReader = docReader.GetPageReader(pageIndex);

            int width = pageReader.GetPageWidth();
            int height = pageReader.GetPageHeight();
            byte[] rawBgra = pageReader.GetImage();

            if (rawBgra is null || rawBgra.Length == 0 || width <= 0 || height <= 0)
            {
                return Task.FromResult<PdfRenderResult>(Falhou(
                    "Página vazia ou inacessível no PDFium.",
                    "DOCNET_EMPTY_PAGE",
                    dpi, sw.ElapsedMilliseconds, utcInicio));
            }

            var ms = BgraParaPng(rawBgra, width, height);
            sw.Stop();

            return Task.FromResult<PdfRenderResult>(new PdfRenderResult.Sucedido(ms, new PdfRenderMetrics
            {
                NomeRenderer = NomeRenderer,
                DpiRealizado = dpi,
                DuracaoMs = sw.ElapsedMilliseconds,
                ViouFallback = false,
                OcorreuEmUtc = utcInicio
            }));
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult<PdfRenderResult>(Falhou(
                "Cancelado pelo chamador.", "DOCNET_CANCELLED",
                dpi, sw.ElapsedMilliseconds, utcInicio));
        }
        catch (Exception ex)
        {
            return Task.FromResult<PdfRenderResult>(Falhou(
                $"Erro PDFium: {ex.Message}", "DOCNET_EXCEPTION",
                dpi, sw.ElapsedMilliseconds, utcInicio));
        }
    }

    /// <summary>
    /// Converte bytes BGRA (formato nativo do PDFium) para PNG em MemoryStream via SkiaSharp.
    /// PDFium usa alpha premultiplicado. Pixels transparentes são compostos sobre fundo branco
    /// para evitar manchas pretas em PDFs sem background explícito.
    /// </summary>
    private static MemoryStream BgraParaPng(byte[] rawBgra, int width, int height)
    {
        CompositeAlphaSobreBranco(rawBgra);

        using var skBitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        IntPtr pixelPtr = skBitmap.GetPixels();
        Marshal.Copy(rawBgra, 0, pixelPtr, rawBgra.Length);

        using var skImage = SKImage.FromBitmap(skBitmap);
        using var encoded = skImage.Encode(SKEncodedImageFormat.Png, 100);

        var ms = new MemoryStream((int)encoded.Size);
        encoded.SaveTo(ms);
        ms.Position = 0;
        return ms;
    }

    /// <summary>
    /// Compõe pixels BGRA premultiplicados sobre fundo branco, tornando todos opacos.
    /// Elimina manchas pretas causadas por transparência em PDFs sem background.
    /// </summary>
    private static void CompositeAlphaSobreBranco(byte[] bgra)
    {
        for (var i = 0; i < bgra.Length; i += 4)
        {
            var a = bgra[i + 3];
            if (a == 255)
                continue;

            if (a == 0)
            {
                bgra[i] = 255;
                bgra[i + 1] = 255;
                bgra[i + 2] = 255;
                bgra[i + 3] = 255;
                continue;
            }

            var invA = 255 - a;
            bgra[i] = (byte)Math.Min(255, bgra[i] + invA);
            bgra[i + 1] = (byte)Math.Min(255, bgra[i + 1] + invA);
            bgra[i + 2] = (byte)Math.Min(255, bgra[i + 2] + invA);
            bgra[i + 3] = 255;
        }
    }

    private PdfRenderResult.Falhou Falhou(
        string mensagem, string codigo, int dpi, long ms, DateTimeOffset utc) =>
        new(mensagem, codigo, new PdfRenderMetrics
        {
            NomeRenderer = NomeRenderer,
            DpiRealizado = dpi,
            DuracaoMs = ms,
            ViouFallback = false,
            OcorreuEmUtc = utc
        });
}
