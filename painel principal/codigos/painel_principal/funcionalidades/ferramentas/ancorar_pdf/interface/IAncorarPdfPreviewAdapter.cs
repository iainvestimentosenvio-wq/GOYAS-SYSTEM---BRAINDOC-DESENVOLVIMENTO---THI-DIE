using System;
using System.Collections.Generic;
using System.Linq;
using Protons.Core.Tarefas.Models;

namespace Protons.UI.Painel.Funcionalidades.Ferramentas.AncorarPdf.Interface;

public sealed record AncorarPdfPreviewSelection(
    int Pagina,
    double XRel,
    double YRel,
    double LarguraRel,
    double AlturaRel,
    bool ViaTexto = false);

public sealed record AncorarPdfPreviewViewportState(
    double Zoom = 1.0,
    double ScrollXRel = 0.0,
    double ScrollYRel = 0.0);

public sealed record AncorarPdfPreviewDocumentoState(
    string? PdfPath,
    IReadOnlyList<AncorarPdfTemplateAncora> Ancoras,
    double HighlightOpacity,
    AncorarPdfPreviewViewportState Viewport,
    string? ChaveAncoraDestacada,
    string? CorAtiva,
    int PaginaAtual = 1,
    int TotalPaginas = 1);

public interface IAncorarPdfPreviewAdapter
{
    string NomeAdapter { get; }
    bool SuportaDocumentoRenderizado { get; }
    AncorarPdfPreviewDocumentoState Snapshot { get; }

    event EventHandler<AncorarPdfPreviewSelection>? SelecaoCapturada;

    void AtualizarDocumento(string? pdfPath);
    void AtualizarAncoras(IReadOnlyList<AncorarPdfTemplateAncora> ancoras, double highlightOpacity);
    void AtualizarViewport(AncorarPdfPreviewViewportState viewport);
    void AtualizarPagina(int paginaAtual, int totalPaginas);
    void DestacarAncora(string? chaveTecnica);
    void DefinirCorAtiva(string? corHex);
    void SimularSelecaoUsuario(AncorarPdfPreviewSelection selecao);
}

public abstract class AncorarPdfPreviewAdapterBase : IAncorarPdfPreviewAdapter
{
    protected AncorarPdfPreviewAdapterBase(string nomeAdapter, bool suportaDocumentoRenderizado = false)
    {
        NomeAdapter = nomeAdapter;
        SuportaDocumentoRenderizado = suportaDocumentoRenderizado;
    }

    public string NomeAdapter { get; }
    public virtual bool SuportaDocumentoRenderizado { get; }
    public AncorarPdfPreviewDocumentoState Snapshot { get; private set; } = new(null, [], 0.40, new(), null, null, 1, 1);

    public event EventHandler<AncorarPdfPreviewSelection>? SelecaoCapturada;

    public void AtualizarDocumento(string? pdfPath)
    {
        Snapshot = Snapshot with
        {
            PdfPath = LimparTextoOpcional(pdfPath)
        };
    }

    public void AtualizarAncoras(IReadOnlyList<AncorarPdfTemplateAncora> ancoras, double highlightOpacity)
    {
        Snapshot = Snapshot with
        {
            Ancoras = ClonarAncoras(ancoras),
            HighlightOpacity = Math.Clamp(highlightOpacity, 0.30, 0.45)
        };
    }

    public void AtualizarViewport(AncorarPdfPreviewViewportState viewport)
    {
        Snapshot = Snapshot with { Viewport = NormalizarViewport(viewport) };
    }

    public void AtualizarPagina(int paginaAtual, int totalPaginas)
    {
        Snapshot = Snapshot with
        {
            PaginaAtual = Math.Max(1, paginaAtual),
            TotalPaginas = Math.Max(1, totalPaginas)
        };
    }

    public void DestacarAncora(string? chaveTecnica)
    {
        Snapshot = Snapshot with
        {
            ChaveAncoraDestacada = LimparTextoOpcional(chaveTecnica)
        };
    }

    public void DefinirCorAtiva(string? corHex)
    {
        Snapshot = Snapshot with
        {
            CorAtiva = LimparTextoOpcional(corHex)
        };
    }

    public void SimularSelecaoUsuario(AncorarPdfPreviewSelection selecao)
    {
        SelecaoCapturada?.Invoke(this, NormalizarSelecao(selecao));
    }

    protected static string? LimparTextoOpcional(string? valor)
    {
        return string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
    }

    protected static IReadOnlyList<AncorarPdfTemplateAncora> ClonarAncoras(IReadOnlyList<AncorarPdfTemplateAncora> ancoras)
    {
        if (ancoras.Count == 0)
            return [];

        return ancoras
            .Select(x => x with { Metadado = x.Metadado with { } })
            .ToArray();
    }

    protected static AncorarPdfPreviewSelection NormalizarSelecao(AncorarPdfPreviewSelection selecao)
    {
        return selecao with
        {
            Pagina = Math.Max(1, selecao.Pagina),
            XRel = Math.Clamp(selecao.XRel, 0, 1),
            YRel = Math.Clamp(selecao.YRel, 0, 1),
            LarguraRel = Math.Clamp(selecao.LarguraRel, 0, 1),
            AlturaRel = Math.Clamp(selecao.AlturaRel, 0, 1)
        };
    }

    protected static AncorarPdfPreviewViewportState NormalizarViewport(AncorarPdfPreviewViewportState viewport)
    {
        return viewport with
        {
            Zoom = Math.Clamp(viewport.Zoom, 0.50, 4.00),
            ScrollXRel = Math.Clamp(viewport.ScrollXRel, 0, 1),
            ScrollYRel = Math.Clamp(viewport.ScrollYRel, 0, 1)
        };
    }
}

public sealed class AncorarPdfNullPreviewAdapter : AncorarPdfPreviewAdapterBase
{
    public AncorarPdfNullPreviewAdapter() : base("Null")
    {
    }
}

public sealed class AncorarPdfCanvasFallbackPreviewAdapter : AncorarPdfPreviewAdapterBase
{
    public AncorarPdfCanvasFallbackPreviewAdapter() : base("CanvasFallback")
    {
    }
}
