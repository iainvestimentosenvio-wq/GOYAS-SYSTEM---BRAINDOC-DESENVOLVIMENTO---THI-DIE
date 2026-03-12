namespace Protons.Core.Tarefas.Models;

public enum AncorarPdfTextoOrigem
{
    Nativo = 0,
    OcrRegiao = 1,
    OcrPagina = 2,
    CloudOpcional = 3
}

public enum AncorarPdfRotaExtracao
{
    Nativo = 0,
    OcrRegiao = 1,
    OcrPagina = 2,
    CloudOpcional = 3
}

public sealed record AncorarPdfTokenAnalise(
    string Texto,
    BboxRelativo Bbox,
    AncorarPdfTextoOrigem Origem,
    double ConfiancaFonte = 1.0);

public sealed record AncorarPdfPaginaAnalise(
    int Numero,
    double LarguraPt,
    double AlturaPt,
    IReadOnlyList<AncorarPdfTokenAnalise> Tokens,
    AncorarPdfRotaExtracao RotaExtracao,
    double CoberturaTextoNativo,
    int TokensNativos,
    int TokensOcr,
    bool OcrExecutado,
    string MotivoRota,
    bool RotacaoDetectada = false,
    double QualidadePagina = 1.0)
{
    public PdfPaginaTexto ToPdfPaginaTextoMesclada() =>
        new(Numero, LarguraPt, AlturaPt, Tokens.Select(t => new PdfPalavra(t.Texto, t.Bbox)).ToArray());
}

public sealed record AncorarPdfExtracaoDiagnostico(
    int Pagina,
    AncorarPdfRotaExtracao RotaExtracao,
    double CoberturaTextoNativo,
    int TokensNativos,
    int TokensOcr,
    bool OcrExecutado,
    string MotivoRota,
    long LatenciaNativoMs,
    long LatenciaOcrMs,
    IReadOnlyList<string> Avisos);

public sealed record AncorarPdfDocumentoAnalise(
    string ArquivoPath,
    IReadOnlyList<AncorarPdfPaginaAnalise> Paginas,
    IReadOnlyList<AncorarPdfExtracaoDiagnostico> Diagnosticos,
    string? FamiliaDocumento = null)
{
    public int TotalPaginas => Paginas.Count;

    public IReadOnlyList<PdfPaginaTexto> ToPdfPaginasTextoMescladas() =>
        Paginas.Select(p => p.ToPdfPaginaTextoMesclada()).ToArray();

    public AncorarPdfPaginaAnalise? ObterPagina(int numeroPagina) =>
        Paginas.FirstOrDefault(p => p.Numero == numeroPagina);
}

public sealed record AncorarPdfDocumentoAnalyzerOptions
{
    public bool AnalyzerV2Ativo { get; init; } = true;
    public bool HybridOcrAtivo { get; init; }
    public bool MultipageEditorAtivo { get; init; } = true;
    public bool AdvancedAnchorsAtivo { get; init; } = true;
    public bool DiagnosticsOverlayAtivo { get; init; } = true;
    public bool CloudDocumentAiProviderAtivo { get; init; }
    public int OcrDpi { get; init; } = 300;
    public string OcrLang { get; init; } = "por+eng";
    public string? ArquivoHashHint { get; init; }
    public int TimeoutDocumentoMs { get; init; } = 30000;
    public int TimeoutPaginaOcrMs { get; init; } = 8000;
    public int TimeoutRegiaoOcrMs { get; init; } = 1500;
}

public sealed record AncorarPdfAnaliseRegiaoRequest(
    int Pagina,
    BboxRelativo Regiao,
    int OcrDpi = 300,
    string OcrLang = "por+eng",
    int TimeoutMs = 1500,
    bool RetryExpandido = true);
