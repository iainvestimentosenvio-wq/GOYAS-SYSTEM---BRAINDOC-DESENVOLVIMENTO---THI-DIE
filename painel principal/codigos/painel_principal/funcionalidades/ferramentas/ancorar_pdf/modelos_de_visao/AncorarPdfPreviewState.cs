using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Protons.Core.Tarefas.Models;
using Protons.Core.Tarefas.Services;
using Protons.UI.Painel.Funcionalidades.Ferramentas.AncorarPdf.Interface;

namespace Protons.UI.Painel.Funcionalidades.Ferramentas.AncorarPdf.ModelosDeVisao;

/// <summary>
/// Gerencia o estado de preview do PDF e orquestra renderizações via <see cref="IPdfPreviewRenderer"/>.
/// <para>
/// Separa as responsabilidades de estado de UI (adapter, viewport, âncoras) da lógica de render,
/// permitindo substituir a estratégia de renderização sem alterar o ViewModel.
/// </para>
/// </summary>
internal sealed class AncorarPdfPreviewState : IDisposable
{
    private readonly IAncorarPdfPreviewAdapter _previewAdapter;
    private readonly IPdfPreviewRenderer _renderer;
    private const int MaxCacheEntries = 20;
    private readonly Dictionary<string, byte[]> _pagePngCache = new(StringComparer.Ordinal);
    private CancellationTokenSource? _renderCts;
    private int _ultimoDpiRenderizado;
    private bool _disposed;

    /// <param name="previewAdapter">Adapter de UI (canvas, WebView, null).</param>
    /// <param name="renderer">
    /// Renderer de PDF. Quando <c>null</c>, usa <see cref="AncorarPdfNullPreviewRenderer"/>
    /// (sem renderização — adequado para testes de ViewModel que não testam render).
    /// Em produção, injete <c>AncorarPdfFallbackPreviewRenderer</c> com a cadeia completa.
    /// </param>
    public AncorarPdfPreviewState(
        IAncorarPdfPreviewAdapter previewAdapter,
        IPdfPreviewRenderer? renderer = null)
    {
        _previewAdapter = previewAdapter;
        _renderer = renderer ?? new AncorarPdfNullPreviewRenderer();
    }

    // ─── Delegação ao adapter ───────────────────────────────────────────────

    public IAncorarPdfPreviewAdapter Adapter => _previewAdapter;
    public string NomeAdapter => _previewAdapter.NomeAdapter;
    public bool PreviewRenderizadoDisponivel => _previewAdapter.SuportaDocumentoRenderizado;
    public bool PreviewFallbackAtivo => !_previewAdapter.SuportaDocumentoRenderizado;
    public AncorarPdfPreviewDocumentoState Snapshot => _previewAdapter.Snapshot;

    public void AtualizarDocumento(string? pdfPath)
    {
        _previewAdapter.AtualizarDocumento(pdfPath);
        if (string.IsNullOrWhiteSpace(pdfPath))
            _pagePngCache.Clear();
    }

    public void AtualizarAncoras(
        IReadOnlyList<AncorarPdfTemplateAncora> ancoras,
        double highlightOpacity)
        => _previewAdapter.AtualizarAncoras(ancoras, highlightOpacity);

    public void AtualizarViewport(AncorarPdfPreviewViewportState viewport)
        => _previewAdapter.AtualizarViewport(viewport);

    public void AtualizarPagina(int paginaAtual, int totalPaginas)
        => _previewAdapter.AtualizarPagina(paginaAtual, totalPaginas);

    public void DestacarAncora(string? chaveTecnica)
        => _previewAdapter.DestacarAncora(chaveTecnica);

    public void DefinirCorAtiva(string? corHex)
        => _previewAdapter.DefinirCorAtiva(corHex);

    // ─── Controle de render ─────────────────────────────────────────────────

    public void ResetRenderState()
    {
        CancelarRenderPendente();
        _ultimoDpiRenderizado = 0;
        _pagePngCache.Clear();
    }

    /// <summary>
    /// Retorna <c>true</c> se o nível de zoom justifica um upgrade de DPI (acima de 2,9×)
    /// e ainda não foi renderizado na resolução máxima.
    /// </summary>
    public bool DeveAgendarUpgrade(double zoomPreview, string? pdfModeloPath)
        => zoomPreview > 2.9 && _ultimoDpiRenderizado < 432 && !string.IsNullOrWhiteSpace(pdfModeloPath);

    /// <summary>
    /// Renderiza a página 1 do PDF via <see cref="IPdfPreviewRenderer"/> e aplica o bitmap resultante.
    /// <para>Fire-and-forget seguro: trata cancelamento e falhas internamente; nunca lança ao chamador.</para>
    /// </summary>
    public async Task RenderizarPdfAsync(
        string? pdfPath,
        int pagina,
        Action<Bitmap?> aplicarBitmap,
        Action<bool> setRenderizando,
        Action<string> setMensagem,
        int dpi = 300,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(pdfPath))
        {
            aplicarBitmap(null);
            return;
        }

        bool pathValido;
        try
        {
            pathValido = File.Exists(pdfPath);
        }
        catch (Exception)
        {
            setMensagem("Caminho do PDF inválido ou inacessível.");
            aplicarBitmap(null);
            return;
        }

        if (!pathValido)
        {
            aplicarBitmap(null);
            return;
        }

        var cacheKey = BuildPageCacheKey(pdfPath, pagina, dpi);
        if (_pagePngCache.TryGetValue(cacheKey, out var cacheHit))
        {
            aplicarBitmap(CriarBitmap(cacheHit));
            _ultimoDpiRenderizado = dpi;
            return;
        }

        setRenderizando(true);
        try
        {
            var request = new PdfRenderRequest(pdfPath, Pagina: Math.Max(1, pagina), Dpi: dpi);
            var resultado = await _renderer.RenderizarAsync(request, ct);

            if (ct.IsCancellationRequested)
                return;

            if (resultado is PdfRenderResult.Sucedido sucedido)
            {
                var pngBytes = sucedido.ImagemPng.ToArray();
                EvictCacheIfNeeded();
                _pagePngCache[cacheKey] = pngBytes;
                aplicarBitmap(CriarBitmap(pngBytes));
                _ultimoDpiRenderizado = resultado.Metricas.DpiRealizado;
            }
            else if (resultado is PdfRenderResult.Falhou falhou)
            {
                setMensagem($"Não foi possível renderizar o PDF. {falhou.MensagemErro}");
                aplicarBitmap(null);
            }
        }
        catch (OperationCanceledException)
        {
            // cancelamento esperado — não reportar como erro
        }
        catch (Exception ex)
        {
            setMensagem($"Erro ao renderizar preview: {ex.Message}");
            aplicarBitmap(null);
        }
        finally
        {
            setRenderizando(false);
        }
    }

    /// <summary>
    /// Agenda um upgrade de DPI (300→432) após debounce de 600 ms.
    /// Acionado quando o zoom ultrapassa 2,9×.
    /// </summary>
    public async Task AgendarUpgradeRenderAsync(
        double zoomPreview,
        string? pdfModeloPath,
        int paginaAtual,
        Action<Bitmap?> aplicarBitmap,
        Action<bool> setRenderizando,
        Action<string> setMensagem)
    {
        _renderCts?.Cancel();
        _renderCts?.Dispose();
        _renderCts = new CancellationTokenSource();
        var ct = _renderCts.Token;

        try
        {
            await Task.Delay(600, ct);
            if (ct.IsCancellationRequested) return;
            if (!DeveAgendarUpgrade(zoomPreview, pdfModeloPath)) return;

            await RenderizarPdfAsync(pdfModeloPath, paginaAtual, aplicarBitmap, setRenderizando, setMensagem, 432, ct);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            setMensagem($"Erro ao melhorar qualidade do preview: {ex.Message}");
        }
    }

    public void CancelarRenderPendente()
    {
        _renderCts?.Cancel();
        _renderCts?.Dispose();
        _renderCts = null;
    }

    public async Task PrefetchPaginasAdjacentesAsync(
        string? pdfPath,
        int paginaAtual,
        int totalPaginas,
        int dpi = 300,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(pdfPath) || totalPaginas <= 1)
            return;

        foreach (var pagina in new[] { paginaAtual - 1, paginaAtual + 1 })
        {
            if (pagina < 1 || pagina > totalPaginas)
                continue;

            var cacheKey = BuildPageCacheKey(pdfPath, pagina, dpi);
            if (_pagePngCache.ContainsKey(cacheKey))
                continue;

            try
            {
                var request = new PdfRenderRequest(pdfPath, Pagina: pagina, Dpi: dpi);
                var resultado = await _renderer.RenderizarAsync(request, ct);
                if (resultado is PdfRenderResult.Sucedido sucedido)
                {
                    EvictCacheIfNeeded();
                    _pagePngCache[cacheKey] = sucedido.ImagemPng.ToArray();
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AncorarPdfPreviewState] Prefetch falhou para página {pagina}: {ex.GetType().Name}");
            }
        }
    }

    private void EvictCacheIfNeeded()
    {
        while (_pagePngCache.Count >= MaxCacheEntries)
        {
            using var enumerator = _pagePngCache.GetEnumerator();
            if (enumerator.MoveNext())
                _pagePngCache.Remove(enumerator.Current.Key);
            else
                break;
        }
    }

    private static string BuildPageCacheKey(string pdfPath, int pagina, int dpi) =>
        $"{pdfPath}|p={Math.Max(1, pagina)}|dpi={Math.Clamp(dpi, 72, 1200)}";

    private static Bitmap CriarBitmap(byte[] pngBytes)
    {
        var ms = new MemoryStream(pngBytes, writable: false);
        try
        {
            return new Bitmap(ms);
        }
        catch
        {
            ms.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CancelarRenderPendente();
        _pagePngCache.Clear();
    }
}
