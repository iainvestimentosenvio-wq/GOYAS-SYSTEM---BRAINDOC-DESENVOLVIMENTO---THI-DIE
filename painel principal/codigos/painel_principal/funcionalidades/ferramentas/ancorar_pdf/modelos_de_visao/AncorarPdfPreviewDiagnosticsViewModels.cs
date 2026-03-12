using Protons.Core.Tarefas.Models;

namespace Protons.UI.Painel.Funcionalidades.Ferramentas.AncorarPdf.ModelosDeVisao;

public sealed class AncorarPdfTokenPreviewItemViewModel
{
    public required string Texto { get; init; }
    public required AncorarPdfTextoOrigem Origem { get; init; }
    public required double PreviewX { get; init; }
    public required double PreviewY { get; init; }
    public required double PreviewLargura { get; init; }
    public required double PreviewAltura { get; init; }
    public required double ConfiancaFonte { get; init; }
    public required double OverlayOpacity { get; init; }

    public string CorStroke => Origem switch
    {
        AncorarPdfTextoOrigem.Nativo => "#4ADE80",
        AncorarPdfTextoOrigem.OcrRegiao => "#F59E0B",
        AncorarPdfTextoOrigem.OcrPagina => "#60A5FA",
        _ => "#C084FC"
    };

    public string CorFill
    {
        get
        {
            var alpha = Origem switch
            {
                AncorarPdfTextoOrigem.Nativo => "22",
                AncorarPdfTextoOrigem.OcrRegiao => "33",
                AncorarPdfTextoOrigem.OcrPagina => "26",
                _ => "26"
            };

            return $"#{alpha}{CorStroke[1..]}";
        }
    }
}

public sealed class AncorarPdfPaginaDiagnosticoItemViewModel
{
    public required int Pagina { get; init; }
    public required string Rota { get; init; }
    public required string Resumo { get; init; }
    public required string Motivo { get; init; }
    public required bool OcrExecutado { get; init; }
    public required bool BaixaCobertura { get; init; }
}
