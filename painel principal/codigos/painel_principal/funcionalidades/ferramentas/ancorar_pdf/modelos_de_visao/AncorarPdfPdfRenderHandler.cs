using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Protons.Core.Tarefas.Models;
using Protons.Core.Tarefas.Services;

namespace Protons.UI.Painel.Funcionalidades.Ferramentas.AncorarPdf.ModelosDeVisao;

/// <summary>
/// Encapsula renderização de PDF e extração de texto para o preview.
/// Extraído de AncorarPdfConfiguracaoViewModel para reduzir acoplamento.
/// </summary>
internal sealed class AncorarPdfPdfRenderHandler
{
    private readonly AncorarPdfPreviewState _previewState;
    private readonly IAncorarPdfExtratorTexto? _extratorTexto;
    private readonly IAncorarPdfDocumentoAnalyzer? _documentAnalyzer;
    private readonly IAncorarPdfPdfRenderContext _context;
    private readonly Action<string, string?>? _registrarEvento;

    public AncorarPdfPdfRenderHandler(
        AncorarPdfPreviewState previewState,
        IAncorarPdfExtratorTexto? extratorTexto,
        IAncorarPdfDocumentoAnalyzer? documentAnalyzer,
        IAncorarPdfPdfRenderContext context,
        Action<string, string?>? registrarEvento = null)
    {
        _previewState = previewState;
        _extratorTexto = extratorTexto;
        _documentAnalyzer = documentAnalyzer;
        _context = context;
        _registrarEvento = registrarEvento;
    }

    public async Task RenderizarPdfAsync(string? pdfPath, int dpi = 300, CancellationToken ct = default)
    {
        await _previewState.RenderizarPdfAsync(
            pdfPath,
            _context.PaginaPreviewAtual,
            b => AplicarBitmapNaUi(b),
            v => SetNaUi(() => _context.EstaRenderizandoPdf = v),
            v => SetNaUi(() => _context.Mensagem = v),
            dpi,
            ct);
    }

    public async Task CarregarTextoPdfAsync(string pdfPath)
    {
        if (_extratorTexto is null && _documentAnalyzer is null) return;
        if (string.IsNullOrWhiteSpace(pdfPath)) return;

        bool pathValido;
        try
        {
            pathValido = File.Exists(pdfPath);
        }
        catch (Exception ex)
        {
            _registrarEvento?.Invoke("ancorar_pdf_pdf_path_error", $"File.Exists threw: {ex.GetType().Name}");
            return;
        }
        if (!pathValido) return;

        SetNaUi(() => _context.ExtracaoEmAndamento = true);
        try
        {
            if (_documentAnalyzer is not null)
            {
                var analise = await _documentAnalyzer.AnalisarAsync(
                    pdfPath,
                    _context.BuildDocumentoAnalyzerOptions(),
                    CancellationToken.None).ConfigureAwait(false);
                SetNaUi(() =>
                {
                    _context.SetDocumentoAnalise(analise);
                    _context.SetPdfPaginasCache(analise.ToPdfPaginasTextoMescladas());
                    _context.DefinirTotalPaginasPreview(Math.Max(1, analise.TotalPaginas));
                });
            }
            else if (_extratorTexto is not null)
            {
                var paginas = await _extratorTexto.ExtrairAsync(pdfPath, CancellationToken.None).ConfigureAwait(false);
                SetNaUi(() =>
                {
                    _context.SetDocumentoAnalise(null);
                    _context.SetPdfPaginasCache(paginas);
                    _context.DefinirTotalPaginasPreview(Math.Max(1, paginas.Count));
                });
            }
        }
        catch (Exception ex)
        {
            _registrarEvento?.Invoke("ancorar_pdf_pdf_extract_error", $"{ex.GetType().Name}: {ex.Message}");
            SetNaUi(() =>
            {
                _context.Mensagem = "Não foi possível extrair texto do PDF. Verifique se o arquivo está acessível.";
                _context.SetDocumentoAnalise(null);
                _context.SetPdfPaginasCache(null);
                _context.DefinirTotalPaginasPreview(1);
            });
        }
        finally
        {
            SetNaUi(() => _context.ExtracaoEmAndamento = false);
        }
    }

    private void AplicarBitmapNaUi(Bitmap? bitmap)
    {
        if (Dispatcher.UIThread.CheckAccess())
            _context.AplicarBitmap(bitmap);
        else
            Dispatcher.UIThread.Post(() => _context.AplicarBitmap(bitmap));
    }

    private void SetNaUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }

    public async Task AgendarUpgradeRenderAsync()
    {
        await _previewState.AgendarUpgradeRenderAsync(
            _context.ZoomPreview,
            _context.PdfModeloPath,
            _context.PaginaPreviewAtual,
            AplicarBitmapNaUi,
            v => SetNaUi(() => _context.EstaRenderizandoPdf = v),
            v => SetNaUi(() => _context.Mensagem = v));
    }

    public async Task PrefetchPaginasAdjacentesAsync(CancellationToken ct = default)
    {
        await _previewState.PrefetchPaginasAdjacentesAsync(
            _context.PdfModeloPath,
            _context.PaginaPreviewAtual,
            _context.TotalPaginasPreview,
            ct: ct);
    }
}

internal interface IAncorarPdfPdfRenderContext
{
    bool EstaRenderizandoPdf { get; set; }
    string Mensagem { get; set; }
    bool ExtracaoEmAndamento { get; set; }
    double ZoomPreview { get; }
    string PdfModeloPath { get; }
    int PaginaPreviewAtual { get; }
    int TotalPaginasPreview { get; }
    void AplicarBitmap(Bitmap? bitmap);
    void SetPdfPaginasCache(IReadOnlyList<PdfPaginaTexto>? cache);
    void SetDocumentoAnalise(AncorarPdfDocumentoAnalise? analise);
    void DefinirTotalPaginasPreview(int totalPaginas);
    AncorarPdfDocumentoAnalyzerOptions BuildDocumentoAnalyzerOptions();
}
