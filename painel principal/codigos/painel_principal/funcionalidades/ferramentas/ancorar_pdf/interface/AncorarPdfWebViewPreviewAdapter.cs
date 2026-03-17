using System;
using System.IO;

namespace Protons.UI.Painel.Funcionalidades.Ferramentas.AncorarPdf.Interface;

public sealed class AncorarPdfWebViewPreviewAdapter : AncorarPdfPreviewAdapterBase
{
    private readonly bool _forceFallback;

    public AncorarPdfWebViewPreviewAdapter(bool forceFallback = false)
        : base("WebViewPdfJs")
    {
        _forceFallback = forceFallback;
    }

    public override bool SuportaDocumentoRenderizado => !_forceFallback && !OperatingSystem.IsLinux();

    public string? ViewerHostUri => BuildViewerHostUri(Snapshot.PdfPath);

    private static string? BuildViewerHostUri(string? pdfPath)
    {
        var viewerPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AncorarPdfViewer", "index.html");
        try
        {
            if (!File.Exists(viewerPath))
                return null;
        }
        catch (Exception)
        {
            return null;
        }

        var baseUri = new Uri(viewerPath).AbsoluteUri;
        if (string.IsNullOrWhiteSpace(pdfPath))
            return baseUri;

        var encoded = Uri.EscapeDataString(pdfPath.Trim());
        return $"{baseUri}?pdf={encoded}";
    }
}
